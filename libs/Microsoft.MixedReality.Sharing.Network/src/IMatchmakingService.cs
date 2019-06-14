﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.MixedReality.Sharing.Network
{
    public interface IMatchmakingService
    {
        /// <summary>
        /// Join a random available existing room.
        /// </summary>
        /// <param name="expectedProperties">Only consider the rooms that have these properties.</param>
        Task<IMatchRoom> JoinRandomRoomAsync(Dictionary<string, object> expectedProperties = null,
            CancellationToken token = default);

        /// <summary>
        /// Room manager. Can be null if the implementation does not provide room managing services.
        /// </summary>
        IMatchRoomManager RoomManager { get; }
    }
}
