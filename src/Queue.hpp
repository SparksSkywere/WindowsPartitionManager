#pragma once

#include "Executor.hpp"
#include "Log.hpp"
#include "Models.hpp"

#include <atomic>
#include <functional>
#include <vector>

namespace pm {

class Queue {
public:
    Queue(Log& log, Storage& storage) : log_(log), storage_(storage) {}
    DiskLayout live;
    DiskLayout working;
    std::vector<PendingOperation> ops;
    int nextId = 1;

    void refresh(const DisplaySettings& display);
    void enqueue(PendingOperation op);
    void undoLast();
    void discardAll();
    OperationResult apply(std::atomic<bool>* cancel, const std::function<void(int)>& progress);
    bool hasPending() const { return !ops.empty(); }

private:
    void rebuild();
    Log& log_;
    Storage& storage_;
};

} // namespace pm
