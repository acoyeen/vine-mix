#include "am/mix_clock.hpp"
#include <array>
#include <cstdint>
#include <iostream>
#include <stdexcept>
#include <string>

// Pure synthetic QPC timing only. No sleeping, audio endpoints, settings, or
// processing effects are involved.
namespace {
std::uint64_t checks{};
void expect(bool condition, const std::string& message) {
    ++checks;
    if (!condition) throw std::runtime_error(message);
}

void absoluteCadence() {
    constexpr std::int64_t period = 50000; // 5 ms at 10 MHz QPC frequency.
    constexpr std::int64_t origin = 123456789;
    constexpr std::array<std::int64_t, 5> wakeLateness{0, 700, 6000, 2500, 1300};
    am::MixClock clock(period, origin);
    auto now = origin;
    for (std::int64_t block = 1; block <= 20000; ++block) {
        const auto next = clock.advance(now + 1000); // 0.1 ms processing.
        expect(next == origin + block * period,
            "ordinary late wakeups retain the absolute 5 ms timeline for 100 seconds");
        now = next + wakeLateness[static_cast<std::size_t>(block) % wakeLateness.size()];
    }
}

void shortDeadlineMiss() {
    am::MixClock clock(50, 0);
    expect(clock.advance(1) == 50, "initial block schedules a single period");
    // The thread wakes 7 ms late for the second block, so a third block is
    // already due when it finishes. Process it immediately, then wait for the
    // original 15 ms boundary instead of permanently moving the clock.
    expect(clock.advance(121) == 100, "one missed block is immediately eligible");
    expect(clock.advance(122) == 150, "one catch-up block restores the original timeline");
    expect(clock.advance(151) == 200, "ordinary scheduling continues after catch-up");

    am::MixClock exact(50, 0);
    expect(exact.advance(50) == 50, "a deadline due exactly now needs no extra timer wait");
    expect(exact.advance(51) == 100, "exact deadline recovery retains phase");
}

void sustainedLateWakeups() {
    constexpr std::int64_t period = 50;
    constexpr std::int64_t duration = 10000; // 1 second, ticks of 0.1 ms.
    am::MixClock clock(period, 0);
    std::int64_t now{};
    std::uint64_t blocks{};
    std::uint64_t consecutiveCatchUps{};
    std::uint64_t longestCatchUp{};
    while (now < duration) {
        ++blocks;
        now += 1;
        const auto next = clock.advance(now);
        if (next <= now) {
            ++consecutiveCatchUps;
            if (consecutiveCatchUps > longestCatchUp) longestCatchUp = consecutiveCatchUps;
        } else {
            consecutiveCatchUps = 0;
            now = next + 70; // Every timer wake is 7 ms late.
        }
    }
    // Despite repeated misses, the number of 240-frame blocks stays within
    // the final wake-up's lateness of the 200 blocks needed per second. A
    // relative now+period reset loses sustained throughput in this scenario.
    expect(blocks >= 198 && blocks <= 200,
        "repeated short deadline misses do not reduce sustained mixer throughput");
    expect(longestCatchUp <= 2, "short-lateness recovery uses at most two immediate blocks");
}

void boundedRecovery() {
    am::MixClock stalled(50, 0);
    expect(stalled.advance(200) == 250, "20 ms stall rebases without replaying stale blocks");
    expect(stalled.advance(251) == 300, "rebased clock continues at normal cadence");

    am::MixClock boundary(50, 0);
    expect(boundary.advance(150) == 200, "two periods of overdue backlog already rebase");

    // Work taking an entire period never catches up. Even when the overdue
    // amount remains small, only two immediate blocks are allowed before a
    // future deadline yields the thread again.
    am::MixClock overloaded(50, 0);
    expect(overloaded.advance(51) == 50, "first bounded catch-up is allowed");
    expect(overloaded.advance(101) == 100, "second bounded catch-up is allowed");
    expect(overloaded.advance(151) == 201, "sustained overload cannot create an endless busy loop");

    overloaded.reset(10000); // Existing >250 ms resume recovery resets graph/clock together.
    expect(overloaded.advance(10001) == 10050, "resume reset drops stale deadlines and catch-up budget");
    expect(overloaded.advance(10101) == 10100, "resume reset restores the catch-up budget");
}
} // namespace

int main() {
    try {
        absoluteCadence(); shortDeadlineMiss(); sustainedLateWakeups(); boundedRecovery();
        std::cout << "PASS " << checks << " mixer clock assertions; absolute cadence, short misses, bounded stall/overload recovery; no hardware or settings accessed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "FAIL after " << checks << " assertions: " << error.what() << '\n';
        return 1;
    }
}
