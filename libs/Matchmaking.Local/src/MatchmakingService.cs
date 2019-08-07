﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("Matchmaking.Local.Test")]

namespace Microsoft.MixedReality.Sharing.Matchmaking.Local
{
    /// <summary>
    /// Simple matchmaking service for local networks.
    /// </summary>
    ///
    /// <remarks>
    /// Rooms are created and stored by the clients themselves. A room is open as long as its owner
    /// is connected and in the room. On room creation, the owner broadcasts a ROOM packet containing the room details.
    ///
    /// Clients who are looking for a room broadcast a FIND packet. Each owner replies with a ROOM
    /// packet for each room it owns.
    ///
    /// On the owner every room corresponds to a TCP port listening for connections. Other participants
    /// can join a room by making a connection to its port.
    ///
    /// * TODO this should arguably all be replaced by state sync *
    /// On connection, a participant sends a JOIN packet to the server containing the participant details,
    /// and receives the current list of participants and room attributes. After this, the connection is
    /// used to:
    /// <list type="bullet">
    /// <item><description>
    /// Receive announcements of participants joining or leaving (PARJ and PARL packets)
    /// </description></item>
    /// <item><description>
    /// Send and receive changes to the room attributes (ATTR packets)
    /// </description></item>
    /// <item><description>
    /// Send and receive arbitrary messages (MSSG packets).
    /// </description></item>
    /// </list>
    /// </remarks>
    public class MatchmakingService : IMatchmakingService, IRoomManager, IDisposable
    {
        private volatile OwnedRoom[] ownedRooms_ = new OwnedRoom[0];
        private volatile RoomBase[] joinedRooms_ = new RoomBase[0];
        private List<RoomList> roomLists_ = new List<RoomList>();
        private readonly MatchParticipantFactory participantFactory_;
        private readonly SocketerClient server_;
        private readonly SocketerClient broadcastSender_;

        public IEnumerable<IRoom> JoinedRooms => joinedRooms_;

        public IRoomManager RoomManager => this;

        public MatchmakingService(MatchParticipantFactory participantFactory, string broadcastAddress, ushort localPort, string localAddress = null)
        {
            participantFactory_ = participantFactory;

            server_ = SocketerClient.CreateListener(SocketerClient.Protocol.UDP, localPort, localAddress);
            server_.Message += OnMessage;
            server_.Start();
            broadcastSender_ = SocketerClient.CreateSender(SocketerClient.Protocol.UDP, broadcastAddress, localPort, localAddress);
            broadcastSender_.Start();
        }

        public void Dispose()
        {
            // TODO mark as disposed so that other methods fail
            lock(roomLists_)
            {
                foreach (var list in roomLists_)
                {
                    list.Dispose();
                }
            }

            foreach (var room in joinedRooms_)
            {
                room.Dispose();
            }

            server_.Stop();
            broadcastSender_.Stop();
        }

        // Send the room that match the passed filter to the passed host.
        private void SendRooms(string host, int port, Func<OwnedRoom, bool> filter)
        {
            // TODO should just use one socket to send udp messages
            SocketerClient replySocket = SocketerClient.CreateSender(SocketerClient.Protocol.UDP, host, port);
            replySocket.Start();
            foreach (var room in ownedRooms_.Where(filter))
            {
                var packet = Utils.CreateRoomPacket(room);
                replySocket.SendNetworkMessage(packet);
            }
            replySocket.Stop();
        }

        private void OnMessage(SocketerClient server, SocketerClient.MessageEvent ev)
        {
            switch (Utils.ParseHeader(ev.Message))
            {
                case Utils.FindByAttrHeader:
                {
                    // Reply with the rooms owned by the local participant that match the queried attributes.
                    var attributes = Utils.ParseFindByAttributesPacket(ev.Message);
                    SendRooms(ev.SourceHost, server.Port,
                        r => r.Visibility == RoomVisibility.Searchable && r.MatchesAttributes(attributes));
                    break;
                }
                case Utils.FindByOwnerHeader:
                {
                    // Reply with the rooms owned by the local participant if it matches the owner.
                    string ownerId = Utils.ParseFindByOwnerPacket(ev.Message);

                    if (ownerId.Equals(participantFactory_.LocalParticipantId))
                    {
                        SendRooms(ev.SourceHost, server.Port,
                            r => r.Visibility == RoomVisibility.Searchable ||
                            r.Visibility == RoomVisibility.ByParticipantOnly);
                    }
                    break;
                }
                case Utils.FindByParticipantsHeader:
                {
                    // Reply with the rooms owned by the local participant that match the queried participants.
                    IEnumerable<string> participantIds = Utils.ParseFindByParticipantsPacket(ev.Message);

                    SendRooms(ev.SourceHost, server.Port,
                        r => (r.Visibility == RoomVisibility.Searchable ||
                        r.Visibility == RoomVisibility.ByParticipantOnly) &&
                        r.Participants.Select(p => p.MatchParticipant.Id).Intersect(participantIds).Any());
                    break;
                }
                case Utils.FindByIdHeader:
                {
                    // Reply with the rooms owned by the local participant that have the queried ID (should be just one).
                    Guid id = Utils.ParseFindByIdPacket(ev.Message);

                    SendRooms(ev.SourceHost, server.Port, r => r.Guid.Equals(id));
                    break;
                }
            }
        }

        public Task<IRoom> CreateRoomAsync(Dictionary<string, object> attributes = null, RoomVisibility visibility = RoomVisibility.NotVisible, CancellationToken token = default)
        {
            return Task.Run<IRoom>(() =>
            {
                // Make a new room.
                SocketerClient roomServer = SocketerClient.CreateListener(SocketerClient.Protocol.TCP, 0, server_.Host);
                var localParticipant = participantFactory_.LocalParticipant;
                var newRoom = new OwnedRoom(this, roomServer, attributes, visibility, localParticipant);

                lock(this)
                {
                    ownedRooms_ = ownedRooms_.Append(newRoom).ToArray();
                    joinedRooms_ = joinedRooms_.Append(newRoom).ToArray();
                }

                // Advertise it.
                if (visibility == RoomVisibility.Searchable)
                {
                    broadcastSender_.SendNetworkMessage(Utils.CreateRoomPacket(newRoom));
                }

                return newRoom;
            }, token);
        }

        // Periodically sends FIND packets and collects results until it is disposed.
        // TODO should remove rooms after a timeout
        private class RoomList : IRoomList
        {
            // Interval between two FIND packets.
            private const int broadcastIntervalMs_ = 2000;

            private volatile MatchmakingService service_;
            private volatile RoomInfo[] activeRooms_ = new RoomInfo[0];
            private readonly CancellationTokenSource sendCts_ = new CancellationTokenSource();

            public event EventHandler<IEnumerable<IRoomInfo>> RoomsRefreshed;

            public RoomList(MatchmakingService service, byte[] packet)
            {
                service_ = service;
                service_.server_.Message += OnMessage;
                var token = sendCts_.Token;

                // Start periodically sending FIND requests
                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        service_.broadcastSender_.SendNetworkMessage(packet);
                        token.WaitHandle.WaitOne(broadcastIntervalMs_);
                    }
                }, token);

                lock(service_.roomLists_)
                {
                    service_.roomLists_.Add(this);
                }
            }

            private void OnMessage(SocketerClient server, SocketerClient.MessageEvent ev)
            {
                var service = service_;
                if (service == null)
                {
                    // The list has beed disposed.
                }

                if (Utils.ParseHeader(ev.Message) == Utils.RoomHeader)
                {
                    // TODO shouldn't delay this thread but offload this to a queue
                    RoomInfo newRoom = Utils.ParseRoomPacket(ev.SourceHost, ev.Message, service);

                    var oldRooms = activeRooms_;
                    RoomInfo[] newRooms = null;
                    int index = oldRooms.ToList().FindIndex(r => r.Id.Equals(newRoom.Id));
                    if (index >= 0)
                    {
                        // TODO check if equal
                        // TODO check timestamp
                        var oldRoom = activeRooms_[index];
                        newRooms = (RoomInfo[])oldRooms.Clone();
                        newRooms[index] = newRoom;
                    }
                    else
                    {
                        newRooms = oldRooms.Append(newRoom).ToArray();
                    }
                    activeRooms_ = newRooms;
                    RoomsRefreshed?.Invoke(this, newRooms);
                }
            }

            public void Dispose()
            {
                lock (service_.roomLists_)
                {
                    service_.roomLists_.Remove(this);
                }

                sendCts_.Cancel();
                service_.server_.Message -= OnMessage;
                service_ = null;
                activeRooms_ = null;
            }

            public IEnumerable<IRoomInfo> CurrentRooms
            {
                get
                {
                    return activeRooms_;
                }
            }
        }

        public IRoomList FindRoomsByAttributes(Dictionary<string, object> attributes = null)
        {
            return new RoomList(this, Utils.CreateFindByAttributesPacket(attributes));
        }

        public IRoomList FindRoomsByOwner(IMatchParticipant owner)
        {
            return new RoomList(this, Utils.CreateFindByOwnerPacket(owner));
        }

        public IRoomList FindRoomsByParticipants(IEnumerable<IMatchParticipant> participants)
        {
            return new RoomList(this, Utils.CreateFindByParticipantsPacket(participants));
        }

        public static void Shuffle<T>(IList<T> list, Random random)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public Task<IRoom> JoinRandomRoomAsync(Dictionary<string, object> expectedAttributes = null, CancellationToken token = default)
        {
            return Task.Run(async () =>
            {
                using (var roomList = new RoomList(this, Utils.CreateFindByAttributesPacket(expectedAttributes)))
                {
                    var random = new Random();

                    IList<RoomInfo> currentRooms = null;
                    var roomsUpdated = new AutoResetEvent(false);

                    EventHandler<IEnumerable<IRoomInfo>> handler = (object l, IEnumerable<IRoomInfo> rooms) =>
                    {
                        currentRooms = (IList<RoomInfo>)rooms;
                        roomsUpdated.Set();
                    };
                    roomList.RoomsRefreshed += handler;

                    while (true)
                    {
                        WaitHandle.WaitAny(new WaitHandle[] { roomsUpdated, token.WaitHandle });
                        token.ThrowIfCancellationRequested();

                        if (currentRooms.Any())
                        {
                            Shuffle(currentRooms, random);
                            foreach (var roomInfo in currentRooms)
                            {
                                var room = await roomInfo.JoinAsync(token);
                                if (room != null)
                                {
                                    return room;
                                }
                            }
                        }
                    }
                }
            }, token);
        }

        public async Task<IRoom> JoinRoomByIdAsync(string roomId, CancellationToken token = default)
        {
            using (var roomList = new RoomList(this, Utils.CreateFindByIdPacket(Guid.Parse(roomId))))
            {
                var roomFuture = new TaskCompletionSource<IRoomInfo>();
                EventHandler<IEnumerable<IRoomInfo>> handler = (object l, IEnumerable<IRoomInfo> rooms) =>
                {
                    if (rooms.Any())
                    {
                        roomFuture.SetResult(rooms.First());
                    }
                };
                roomList.RoomsRefreshed += handler;
                token.Register(() => roomFuture.SetCanceled());
                IRoomInfo roomInfo = await roomFuture.Task;
                return await roomInfo.JoinAsync(token);
            }
        }

        internal Task<IRoom> JoinAsync(RoomInfo roomInfo, CancellationToken token)
        {
            return Task.Run<IRoom>(() =>
            {
                // todo logic is split between here and ForeignRoom ctor, cleanup
                // Make a socket.
                SocketerClient socket = SocketerClient.CreateSender(SocketerClient.Protocol.TCP, roomInfo.Host, roomInfo.Port);

                // Make a room.
                var res = new ForeignRoom(roomInfo, null /* TODO */, socket);

                // Configure handlers and try to connect.
                var ev = new ManualResetEventSlim();
                Action<SocketerClient, int, string, int> connectHandler =
                (SocketerClient server, int id, string clientHost, int clientPort) =>
                {
                    // Connected; add the room to the joined list.
                    lock (this)
                    {
                        if (joinedRooms_.Any(r => r.Guid == roomInfo.Guid))
                        {
                            throw new InvalidOperationException("Room " + roomInfo.Guid + " is already joined");
                        }
                        joinedRooms_ = joinedRooms_.Append(res).ToArray();
                    }
                    // Wake up the original task.
                    ev.Set();
                };
                socket.Connected += connectHandler;
                socket.Disconnected += (SocketerClient server, int id, string clientHost, int clientPort) =>
                {
                    lock(this)
                    {
                        joinedRooms_ = joinedRooms_.Where(r => r != res).ToArray();
                    }
                    socket.Stop();
                };
                socket.Start();
                ev.Wait(token);
                socket.Connected -= connectHandler;

                // Send participant info to the server.
                socket.SendNetworkMessage(Utils.CreateJoinRequestPacket(participantFactory_.LocalParticipant));

                // Now that the connection is established, we can return the room.
                return res;
            }, token);
        }
    }
}
