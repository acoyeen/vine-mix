//! Absolute mixer deadlines (port of `native/include/am/mix_clock.hpp`).
//!
//! Deadlines are in QueryPerformanceCounter ticks. Small scheduler delays must
//! not slow audio production by adding a fresh block-sized wait. Catch-up is
//! limited so an overloaded or stalled mixer never replays an unbounded
//! backlog of old blocks in a burst.

#[derive(Clone, Copy, Debug)]
pub struct MixClock {
    ticks_per_block: i64,
    next: i64,
    catch_up_blocks: u32,
}

impl MixClock {
    const MAXIMUM_CATCH_UP_BLOCKS: u32 = 2;

    pub const fn new(ticks_per_block: i64, first_block: i64) -> Self {
        Self {
            ticks_per_block: if ticks_per_block > 0 { ticks_per_block } else { 1 },
            next: first_block,
            catch_up_blocks: 0,
        }
    }

    pub fn reset(&mut self, first_block: i64) {
        self.next = first_block;
        self.catch_up_blocks = 0;
    }

    /// Completion deadline of the quantum currently scheduled. Read before
    /// [`MixClock::advance`], which may deliberately rebase a stalled or
    /// overloaded timeline.
    pub const fn completion_deadline(&self) -> i64 {
        self.next + self.ticks_per_block
    }

    /// Call after processing one block. A returned deadline `<= now` means the
    /// next block should run immediately, subject to the caller's stop check.
    #[must_use]
    pub fn advance(&mut self, now: i64) -> i64 {
        self.next += self.ticks_per_block;
        if self.next > now {
            self.catch_up_blocks = 0;
        } else if (now - self.next) / self.ticks_per_block >= i64::from(Self::MAXIMUM_CATCH_UP_BLOCKS)
            || self.catch_up_blocks == Self::MAXIMUM_CATCH_UP_BLOCKS
        {
            self.next = now + self.ticks_per_block;
            self.catch_up_blocks = 0;
        } else {
            self.catch_up_blocks += 1;
        }
        self.next
    }
}

/// Port of `native/tests/mix_clock_tests.cpp`.
#[cfg(test)]
mod tests {
    use super::MixClock;

    #[test]
    fn absolute_cadence() {
        const PERIOD: i64 = 50_000; // 5 ms at a 10 MHz QPC frequency.
        const ORIGIN: i64 = 123_456_789;
        const WAKE_LATENESS: [i64; 5] = [0, 700, 6000, 2500, 1300];
        let mut clock = MixClock::new(PERIOD, ORIGIN);
        let mut now = ORIGIN;
        for block in 1..=20_000i64 {
            let next = clock.advance(now + 1000); // 0.1 ms processing.
            assert_eq!(next, ORIGIN + block * PERIOD, "late wakeups keep the absolute timeline");
            now = next + WAKE_LATENESS[(block as usize) % WAKE_LATENESS.len()];
        }
    }

    #[test]
    fn short_deadline_miss() {
        let mut clock = MixClock::new(50, 0);
        assert_eq!(clock.advance(1), 50, "initial block schedules a single period");
        assert_eq!(clock.advance(121), 100, "one missed block is immediately eligible");
        assert_eq!(clock.advance(122), 150, "one catch-up block restores the original timeline");
        assert_eq!(clock.advance(151), 200, "ordinary scheduling continues after catch-up");

        let mut exact = MixClock::new(50, 0);
        assert_eq!(exact.advance(50), 50, "a deadline due exactly now needs no extra wait");
        assert_eq!(exact.advance(51), 100, "exact deadline recovery retains phase");
    }

    #[test]
    fn sustained_late_wakeups() {
        const PERIOD: i64 = 50;
        const DURATION: i64 = 10_000; // 1 second in 0.1 ms ticks.
        let mut clock = MixClock::new(PERIOD, 0);
        let (mut now, mut blocks, mut consecutive, mut longest) = (0i64, 0u64, 0u64, 0u64);
        while now < DURATION {
            blocks += 1;
            now += 1;
            let next = clock.advance(now);
            if next <= now {
                consecutive += 1;
                longest = longest.max(consecutive);
            } else {
                consecutive = 0;
                now = next + 70; // Every timer wake is 7 ms late.
            }
        }
        assert!((198..=200).contains(&blocks), "repeated misses keep sustained throughput: {blocks}");
        assert!(longest <= 2, "short-lateness recovery uses at most two immediate blocks");
    }

    #[test]
    fn bounded_recovery() {
        let mut stalled = MixClock::new(50, 0);
        assert_eq!(stalled.advance(200), 250, "20 ms stall rebases without replaying stale blocks");
        assert_eq!(stalled.advance(251), 300, "rebased clock continues at normal cadence");

        let mut boundary = MixClock::new(50, 0);
        assert_eq!(boundary.advance(150), 200, "two periods of overdue backlog already rebase");

        let mut overloaded = MixClock::new(50, 0);
        assert_eq!(overloaded.advance(51), 50, "first bounded catch-up is allowed");
        assert_eq!(overloaded.advance(101), 100, "second bounded catch-up is allowed");
        assert_eq!(overloaded.advance(151), 201, "sustained overload cannot busy-loop");

        overloaded.reset(10_000);
        assert_eq!(overloaded.advance(10_001), 10_050, "reset drops stale deadlines and budget");
        assert_eq!(overloaded.advance(10_101), 10_100, "reset restores the catch-up budget");
    }

    #[test]
    fn non_positive_period_is_clamped() {
        let mut clock = MixClock::new(0, 10);
        assert_eq!(clock.advance(0), 11);
    }
}
