#pragma once

#include <functional>
#include <string>
#include <vector>

namespace pm {

class Log {
public:
    void info(const std::wstring& line);
    void warn(const std::wstring& line);
    void error(const std::wstring& line);
    void success(const std::wstring& line);
    const std::vector<std::wstring>& lines() const { return lines_; }
    void clear() { lines_.clear(); }
    std::function<void(const std::wstring&)> onLine;

private:
    void add(const wchar_t* prefix, const std::wstring& line);
    std::vector<std::wstring> lines_;
};

} // namespace pm
