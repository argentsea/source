using ArgentSea.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Reminders;

namespace ArgentSea.Orleans.Test
{
    /// <summary>
    /// Spec section orleans-reminder-storage, the part that lives in ArgentSea.Orleans: ArgentSeaReminderOptionsValidator.
    /// (The reminder table and its registration are tested in ArgentSea.Orleans.Sql.Test.)
    /// </summary>
    public class ReminderOptionsValidatorTests
    {
        private static (ArgentSeaReminderOptionsValidator validator, RecordingLogger<ArgentSeaReminderOptionsValidator> logger) Build(TimeSpan minimumReminderPeriod)
        {
            var logger = new RecordingLogger<ArgentSeaReminderOptionsValidator>();
            var validator = new ArgentSeaReminderOptionsValidator(logger, Options.Create(new ReminderOptions { MinimumReminderPeriod = minimumReminderPeriod }));
            return (validator, logger);
        }

        [Fact]
        [Trait("Spec", "blk-691a21722e7945909232c2031ee93d2d")]
        [Trait("Spec", "blk-c972c7dfb3f645c2b834364af7a5e185")]
        [Trait("Spec", "blk-08938b1b1b694b1d985a9ecf74dc11d4")]
        public void ANegativeMinimumReminderPeriodIsRejected()
        {
            var (validator, logger) = Build(TimeSpan.FromSeconds(-1));

            validator.Invoking(v => v.ValidateConfiguration()).Should().Throw<OrleansConfigurationException>()
                .WithMessage("*MinimumReminderPeriod must not be less than*");
            logger.Entries.Should().BeEmpty();
        }

        [Fact]
        [Trait("Spec", "blk-98e320e2ec3b49189c8c00be74a679b3")]
        [Trait("Spec", "blk-cc9d08cbfe6d48aab59201a4386d2a64")]
        [Trait("Spec", "blk-08938b1b1b694b1d985a9ecf74dc11d4")]
        [Trait("Spec", "blk-bf35b4d116cc48b2a1630eb2af5b12d6")]
        public void APeriodBelowOneMinuteIsAdmittedWithAFastReminderWarning()
        {
            var (validator, logger) = Build(TimeSpan.FromSeconds(30));

            validator.Invoking(v => v.ValidateConfiguration()).Should().NotThrow();

            var warning = logger.Entries.Should().ContainSingle().Which;
            warning.Level.Should().Be(LogLevel.Warning);
            warning.Id.Should().Be((int)RSErrorCode.RS_FastReminderInterval);
            warning.Message.Should().Contain("MinimumReminderPeriod is 0:00:30", "TimeSpan's \"g\" format renders 30 seconds as 0:00:30")
                .And.Contain("default 1", "the floor is ReminderOptionsDefaults.MinimumReminderPeriodMinutes = 1");
        }

        [Fact]
        [Trait("Spec", "blk-98e320e2ec3b49189c8c00be74a679b3")]
        [Trait("Spec", "blk-08938b1b1b694b1d985a9ecf74dc11d4")]
        public void APeriodOfAtLeastOneMinuteIsAdmittedSilently()
        {
            var (validator, logger) = Build(TimeSpan.FromMinutes(1));

            validator.Invoking(v => v.ValidateConfiguration()).Should().NotThrow();
            logger.Entries.Should().BeEmpty();

            var (zero, zeroLogger) = Build(TimeSpan.Zero);
            zero.Invoking(v => v.ValidateConfiguration()).Should().NotThrow("zero is not less than zero");
            zeroLogger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        }
    }
}
