﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.MixedReality.Sharing.Matchmaking;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Xunit;

namespace Matchmaking.Local.Test
{
    public class SimpleTest
    {
        private static int TestTimeoutMs
        {
            get
            {
                return Debugger.IsAttached ? Timeout.Infinite : 10000;
            }
        }

        private void SendAndReceive(IMatchmakingService service)
        {
            // Do simple operations to verify that sending and receiving packets doesn't fail.
            using (var cts = new CancellationTokenSource(TestTimeoutMs))
            {
                var _ = service.CreateRoomAsync("CreateRoom", "http://room1", null, cts.Token).Result;
            }
            using (var task = service.StartDiscovery("Category")) { }
        }

        [Fact]
        public void Broadcast()
        {
            using (var svc1 = new PeerMatchmakingService(new UdpPeerNetwork(new IPAddress(0xffffffff), 45279)))
            {
                SendAndReceive(svc1);
            }
        }
        [Fact]
        public void Multicast()
        {
            using (var svc1 = new PeerMatchmakingService(new UdpPeerNetwork(new IPAddress(0x000000e0), 45280)))
            {
                SendAndReceive(svc1);
            }
        }
    }
}
