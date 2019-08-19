﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Microsoft.MixedReality.Sharing.StateSync
{
    /// <summary>
    /// Implement this interface to receive updates for Key & sub-key value updates.
    /// </summary>
    public interface ISubKeySubscription
    {
        /// <summary>
        /// Invoked when the sub key subscribed for was (re)added to the storage.
        /// </summary>
        /// <param name="key">The key for which the addition occured.</param>
        /// <param name="subKey">The sub key for which the addition occured.</param>
        /// <param name="previousSnapshot">The snapshot prior to the update.</param>
        /// <param name="currentSnapshot">The current snapshot (post the update).</param>
        void SubKeyAdded(RefKey key, ulong subKey, Snapshot previousSnapshot, Snapshot currentSnapshot);

        /// <summary>
        /// Invoked when the value for sub key subscribed for updated in the storage.
        /// </summary>
        /// <param name="key">The key for which the update occured.</param>
        /// <param name="subKey">The sub key for which the update occured.</param>
        /// <param name="previousSnapshot">The snapshot prior to the update.</param>
        /// <param name="currentSnapshot">The current snapshot (post the update).</param>
        void SubKeyUpdated(RefKey key, ulong subKey, Snapshot previousSnapshot, Snapshot currentSnapshot);

        /// <summary>
        /// Invoked when the sub key subscribed for was removed from the storage.
        /// </summary>
        /// <param name="key">The key for which the removal occured.</param>
        /// <param name="subKey">The sub key for which the removal occured.</param>
        /// <param name="previousSnapshot">The snapshot prior to the update.</param>
        /// <param name="currentSnapshot">The current snapshot (post the update).</param>
        void SubKeyRemoved(RefKey key, ulong subKey, Snapshot previousSnapshot, Snapshot currentSnapshot);
    }
}
