#pragma once

#include "Executor.hpp"
#include "Log.hpp"
#include "Models.hpp"

#include <atomic>
#include <functional>

namespace pm {

OperationResult cloneDisk(const CloneDiskParams& p, Storage& storage, Log& log,
                          std::atomic<bool>* cancel, const std::function<void(int)>& progress);
OperationResult clonePartition(const ClonePartitionParams& p, Storage& storage, Log& log,
                               std::atomic<bool>* cancel, const std::function<void(int)>& progress);

} // namespace pm
