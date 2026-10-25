#pragma once

#include "Models.hpp"

namespace pm {

class ConfigStore {
public:
    AppConfig config;
    void load();
    void save() const;
    std::wstring path() const;
};

} // namespace pm
