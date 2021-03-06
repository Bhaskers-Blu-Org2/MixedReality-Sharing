// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once
#include <Microsoft/MixedReality/Sharing/VersionedStorage/KeyIterator.h>
#include <Microsoft/MixedReality/Sharing/VersionedStorage/SubkeyIterator.h>
#include <Microsoft/MixedReality/Sharing/VersionedStorage/VersionedPayloadHandle.h>

#include <optional>

namespace Microsoft::MixedReality::Sharing::VersionedStorage {

class KeyDescriptor;
class Behavior;

struct SnapshotInfo {
  uint64_t version_{0};
  size_t keys_count_{0};
  size_t subkeys_count_{0};
};

// References an immutable state of the storage at some specific version.
// A snapshot can be taken from the Storage at any time, and it is going to
// reference the same state for as long as it's alive.
// There is no specific limit on the number of alive snapshots, although having
// too many may result in the storage running out of memory.
//
// In addition to getters, any snapshot can be iterated over, as if it was a
// range of KeyView objects (see begin() and end() below), in which case the
// iteration will traverse over all keys that have at least one subkey in this
// version. Then GetSubkeys() can be used to iterate over subkeys of each
// found key.
class Snapshot {
 public:
  // Doesn't increment any reference counts (they should be pre-incremented).
  Snapshot(Detail::HeaderBlock& header_block,
           std::shared_ptr<Behavior> behavior,
           const SnapshotInfo& info) noexcept;

  Snapshot() noexcept;
  Snapshot(Snapshot&&) noexcept;
  Snapshot(const Snapshot&) noexcept;

  ~Snapshot() noexcept;

  Snapshot& operator=(Snapshot&&) noexcept;
  Snapshot& operator=(const Snapshot&) noexcept;

  uint64_t version() const noexcept { return info_.version_; }
  size_t keys_count() const noexcept { return info_.keys_count_; }
  size_t subkeys_count() const noexcept { return info_.subkeys_count_; }

  const SnapshotInfo& info() const noexcept { return info_; }

  // Returns a payload handle and a version of the storage when it was assigned.
  // If the subkey doesn't exist in this snapshot, result will be empty.
  // Call has_payload() or operator bool() to check if the subkey was found.
  VersionedPayloadHandle Get(const KeyDescriptor& key, uint64_t subkey) const
      noexcept;

  // Returns the view of the key state if it has any subkeys in this version.
  // Returns an empty optional otherwise.
  // Call GetSubkeys() with the returned KeyView to iterate over subkeys.
  std::optional<KeyView> Get(const KeyDescriptor& key) const noexcept;

  // A shortcut to the method above which returns 0 if the key wasn't found.
  size_t GetSubkeysCount(const KeyDescriptor& key) const noexcept;

  KeyIterator begin() const noexcept { return {*this}; }
  KeyIterator::End end() const noexcept { return {}; }

  // Returns a range of subkeys associated with the provided KeyView that have
  // payloads in this version.
  SubkeyIteratorRange GetSubkeys(const KeyView& key_view) const noexcept {
    return SubkeyIteratorRange{SubkeyIterator{key_view, *this}};
  }

 private:
  Detail::HeaderBlock* header_block_{nullptr};
  std::shared_ptr<Behavior> behavior_;
  SnapshotInfo info_;
  friend class Storage;
  friend class KeyIterator;
  friend class SubkeyIterator;
};

}  // namespace Microsoft::MixedReality::Sharing::VersionedStorage
