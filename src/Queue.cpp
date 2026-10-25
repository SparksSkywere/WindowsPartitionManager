#include "Queue.hpp"
#include "Inventory.hpp"
#include "Preview.hpp"

namespace pm {

void Queue::rebuild() {
    working = applyPreview(live, ops);
}

void Queue::refresh(const DisplaySettings& display) {
    live = queryInventory(display, log_);
    rebuild();
}

void Queue::enqueue(PendingOperation op) {
    op.id = nextId++;
    ops.push_back(std::move(op));
    rebuild();
    log_.info(L"Queued: " + ops.back().description);
}

void Queue::undoLast() {
    if (ops.empty()) return;
    const auto last = ops.back();
    ops.pop_back();
    rebuild();
    log_.info(L"Undid: " + last.description);
}

void Queue::discardAll() {
    if (ops.empty()) return;
    const auto n = ops.size();
    ops.clear();
    rebuild();
    log_.info(L"Discarded " + std::to_wstring(n) + L" pending operation(s).");
}

OperationResult Queue::apply(std::atomic<bool>* cancel, const std::function<void(int)>& progress) {
    if (ops.empty()) return {true, L"Nothing to apply.", 0};
    auto snapshot = ops;
    for (size_t i = 0; i < snapshot.size(); ++i) {
        if (cancel && cancel->load()) return {false, L"Cancelled.", 0};
        if (progress) progress(static_cast<int>(i * 100 / snapshot.size()));
        auto slice = [&](int p) {
            if (progress)
                progress(static_cast<int>((i + p / 100.0) / snapshot.size() * 100));
        };
        const auto result = storage_.execute(snapshot[i], cancel, slice);
        if (!result.success) {
            while (!ops.empty() && ops.front().id != snapshot[i].id) ops.erase(ops.begin());
            if (!ops.empty() && ops.front().id == snapshot[i].id) ops.erase(ops.begin());
            rebuild();
            return result;
        }
    }
    ops.clear();
    rebuild();
    if (progress) progress(100);
    return {true, L"Applied " + std::to_wstring(snapshot.size()) + L" operation(s).", 0};
}

} // namespace pm
