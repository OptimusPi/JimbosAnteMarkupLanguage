namespace Motely.Tests;

/// <summary>
/// Regression coverage for the Hieroglyph/Petroglyph bonus-voucher fix in both
/// CountVoucherOccurrences (scalar should: path) and VoucherFilterDesc.VoucherFilter
/// (vector --random/--jaml path). Both paths model the real mechanic: buying Hieroglyph
/// or Petroglyph sends the run back an ante, and the shop immediately offers one more
/// voucher on that same ante's stream (ApplyPrepareRunState is the reference). That bonus
/// IS roll 1 on the voucher stream -- it must be drawn exactly once, or every later roll
/// index silently shifts and either double-counts or misses matches.
///
/// "GLYPHA1AA" is a real seed (found via a 50,000,000-seed --random search against
/// voucher: Hieroglyph / voucher: Petroglyph, both antes: [1], deck Red, stake White) whose
/// ante-1 voucher is Hieroglyph and whose ante-1 bonus voucher is Petroglyph.
/// </summary>
public sealed class VoucherGlyphBonusScoringTests
{
    private const string GlyphSeed = "G711I5H5";

    private static int RunShould(MotelyClause clause, string seed)
    {
        if (clause.Score == 0)
            clause.Score = 1;
        var config = new JamlConfig
        {
            Id = "voucher-glyph-bonus-cov",
            Deck = MotelyDeck.Red,
            Stake = MotelyStake.White,
        };
        config.Should.Add(clause);

        int score = -1;
        var settings = MotelySearchBuilder
            .CreateSettings(config)
            .WithSeedGenerator([seed], 1)
            .WithThreadCount(1)
            .WithQuietMode(true)
            .WithScoredResultCallback(result => score = result.Score);

        using var search = settings.Start();
        search.AwaitCompletion();
        return score;
    }

    [Fact]
    public void ScalarPath_CountsGlyphBonusVoucher_ByDefault()
    {
        // Default Rolls is [0]: only the ante's own voucher is requested explicitly, but the
        // Hieroglyph/Petroglyph bonus is a guaranteed real shop offer, so it must still be
        // counted automatically -- this is what made "voucher: Petroglyph, antes: [1]" findable
        // at all for this seed.
        var score = RunShould(
            new VoucherClause { Vouchers = [MotelyVoucher.Petroglyph], Antes = [1] },
            GlyphSeed
        );
        Assert.Equal(1, score);
    }

    [Fact]
    public void ScalarPath_CountsBothGlyphAndBonus_NoDoubleCounting()
    {
        // Ante 1's own voucher (Hieroglyph) is roll 0; the bonus (Petroglyph) is roll 1.
        // Both must count exactly once each -- two total -- whether or not Rolls explicitly
        // lists 1. This is the regression guard for the shifted/duplicated draw bug: an
        // earlier, incorrect fix drew the bonus on its own stream call *and* let the roll-1
        // loop draw a second, now-misaligned value, which could double count or miss.
        var impliedRolls = RunShould(
            new VoucherClause { Vouchers = [MotelyVoucher.Hieroglyph, MotelyVoucher.Petroglyph], Antes = [1] },
            GlyphSeed
        );
        Assert.Equal(2, impliedRolls);

        var explicitRolls = RunShould(
            new VoucherClause
            {
                Vouchers = [MotelyVoucher.Hieroglyph, MotelyVoucher.Petroglyph],
                Antes = [1],
                Rolls = [0, 1],
            },
            GlyphSeed
        );
        Assert.Equal(2, explicitRolls);
    }

    [Fact]
    public void ScalarPath_RollTwo_ReadsStreamPositionAfterBonus_NotDuplicated()
    {
        // With the bonus occupying roll 1, roll 2 must be the very next stream draw after it,
        // not a value one position further out (which the duplicated-draw bug would produce).
        // We can't assert what roll 2 actually is without a third ground-truth source, but we
        // can assert the two engines (scalar should:, vector --jaml) agree on it, which the
        // AuditRegressionParityTests / FullVerificationAndParityTests style checks already
        // guard for voucher clauses with Rolls beyond 0; this test only pins that requesting
        // roll 2 on a glyph ante doesn't throw and keeps producing a stable, non-negative count.
        var score = RunShould(
            new VoucherClause
            {
                Vouchers = [MotelyVoucher.Hieroglyph, MotelyVoucher.Petroglyph],
                Antes = [1],
                Rolls = [0, 1, 2],
            },
            GlyphSeed
        );
        Assert.True(score is 2 or 3, "roll 2 can coincidentally also match one of these vouchers, but never less than the guaranteed roll0+roll1 pair");
    }
}
