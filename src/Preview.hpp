#pragma once

#include "Models.hpp"

#include <string>
#include <vector>

namespace pm {

inline constexpr std::uint64_t kAlign = 1024ULL * 1024ULL;

DiskLayout applyPreview(const DiskLayout& live, const std::vector<PendingOperation>& ops);
bool tryPlanDisk(const CloneDiskParams& p, std::vector<PlannedSlice>& plan, std::wstring& error);
bool tryPlanPartition(const ClonePartitionParams& p, PlannedSlice& plan, std::wstring& error);
std::wstring gptTypeFor(const CloneSlice& s);
std::uint16_t mbrTypeFor(const CloneSlice& s);
bool needsCopy(const CloneSlice& s);
std::uint64_t lastAllocatedEnd(const std::vector<CloneSlice>& slices);

} // namespace pm
