using System;
using NUnit.Framework;
using Project001.UI.Digits;

namespace Project001.Tests.EditMode
{
    /// <summary>Pure decomposition/layout tests for DigitLayout — no scene, no MonoBehaviour, no sprites.</summary>
    public class DigitLayoutTests
    {
        // All digits square (aspect 1) except '1', which is narrower — enough to exercise "natural width" behaviour without needing real sprites.
        private static readonly float[] Aspects = { 1f, 0.5f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };

        [Test]
        public void Compute_ForZero_ProducesOneDigitAtOrigin()
        {
            var placements = DigitLayout.Compute(0, Aspects, displayedHeight: 10f, digitGap: 2f, groupGap: 5f);

            Assert.AreEqual(1, placements.Count);
            Assert.AreEqual(0, placements[0].Digit);
            Assert.AreEqual(5f, placements[0].CenterX); // width 10 (aspect 1 * height 10), centered at 5
        }

        [Test]
        public void Compute_For900_ProducesThreeDigitsInOrder_NoGroupGap()
        {
            var placements = DigitLayout.Compute(900, Aspects, displayedHeight: 10f, digitGap: 2f, groupGap: 5f);

            Assert.AreEqual(3, placements.Count);
            Assert.AreEqual(9, placements[0].Digit);
            Assert.AreEqual(0, placements[1].Digit);
            Assert.AreEqual(0, placements[2].Digit);

            // 3 digits total: no thousands-group boundary is crossed, so only the plain digitGap applies between them.
            float expectedGapBetweenCenters = 10f + 2f; // width + digitGap
            Assert.AreEqual(expectedGapBetweenCenters, placements[1].CenterX - placements[0].CenterX, 1e-4f);
            Assert.AreEqual(expectedGapBetweenCenters, placements[2].CenterX - placements[1].CenterX, 1e-4f);
        }

        [Test]
        public void Compute_For1250_InsertsGroupGap_AfterFirstDigit()
        {
            var placements = DigitLayout.Compute(1250, Aspects, displayedHeight: 10f, digitGap: 2f, groupGap: 5f);

            Assert.AreEqual(4, placements.Count);
            Assert.AreEqual(1, placements[0].Digit);
            Assert.AreEqual(2, placements[1].Digit);
            Assert.AreEqual(5, placements[2].Digit);
            Assert.AreEqual(0, placements[3].Digit);

            // Boundary is between digit 0 ('1') and digit 1 ('2') - the last 3 digits ("250") form one group.
            // digit 0 is narrow (aspect 0.5 -> width 5), digit 1 is aspect 1 -> width 10.
            float digit0Width = 10f * 0.5f;
            float digit1Width = 10f;
            float expectedCenterGap = digit0Width / 2f + (2f + 5f) + digit1Width / 2f;
            Assert.AreEqual(expectedCenterGap, placements[1].CenterX - placements[0].CenterX, 1e-4f);

            // No further boundary between digits 1-2 or 2-3.
            Assert.AreEqual(10f + 2f, placements[2].CenterX - placements[1].CenterX, 1e-4f);
            Assert.AreEqual(10f + 2f, placements[3].CenterX - placements[2].CenterX, 1e-4f);
        }

        [Test]
        public void Compute_For125000_InsertsGroupGap_BeforeLastThreeDigits()
        {
            var placements = DigitLayout.Compute(125000, Aspects, displayedHeight: 10f, digitGap: 2f, groupGap: 5f);

            Assert.AreEqual(6, placements.Count);
            int[] expectedDigits = { 1, 2, 5, 0, 0, 0 };
            for (int i = 0; i < expectedDigits.Length; i++)
                Assert.AreEqual(expectedDigits[i], placements[i].Digit);

            // "125 000": boundary between digit index 2 ('5') and index 3 ('0').
            float plainGap = 10f + 2f; // aspect-1 digits either side, except index 0 which is narrow ('1')
            Assert.AreEqual(plainGap, placements[2].CenterX - placements[1].CenterX, 1e-4f);

            float digit2Width = 10f;
            float digit3Width = 10f;
            float expectedBoundaryGap = digit2Width / 2f + (2f + 5f) + digit3Width / 2f;
            Assert.AreEqual(expectedBoundaryGap, placements[3].CenterX - placements[2].CenterX, 1e-4f);

            Assert.AreEqual(plainGap, placements[4].CenterX - placements[3].CenterX, 1e-4f);
            Assert.AreEqual(plainGap, placements[5].CenterX - placements[4].CenterX, 1e-4f);
        }

        [Test]
        public void Compute_NarrowDigit_IsNarrowerThanSquareDigit()
        {
            var placements = DigitLayout.Compute(19, Aspects, displayedHeight: 10f, digitGap: 2f, groupGap: 5f);

            Assert.Less(placements[0].Width, placements[1].Width); // digit '1' (aspect 0.5) narrower than digit '9' (aspect 1)
        }

        [Test]
        public void TotalWidth_MatchesLastDigitsRightEdge()
        {
            var placements = DigitLayout.Compute(125000, Aspects, displayedHeight: 10f, digitGap: 2f, groupGap: 5f);

            float totalWidth = DigitLayout.TotalWidth(placements);
            var last = placements[placements.Count - 1];

            Assert.AreEqual(last.CenterX + last.Width / 2f, totalWidth, 1e-4f);
        }

        [Test]
        public void TotalWidth_ForEmptyList_IsZero()
        {
            Assert.AreEqual(0f, DigitLayout.TotalWidth(Array.Empty<DigitLayout.DigitPlacement>()));
        }

        [Test]
        public void Compute_NegativeValue_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                DigitLayout.Compute(-1, Aspects, 10f, 2f, 5f));
        }

        [Test]
        public void Compute_WrongAspectRatioCount_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                DigitLayout.Compute(0, new float[] { 1f, 1f }, 10f, 2f, 5f));
        }
    }
}
