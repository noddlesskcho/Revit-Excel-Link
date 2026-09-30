using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Sync;
using Xunit;

namespace RevitScheduleSync.Tests
{
    public class ValueComparerTests
    {
        [Theory]
        [InlineData("D101", "D101", "D101", ThreeWay.NoChange)]
        [InlineData("D101", "D101", "D102", ThreeWay.ExcelChange)]
        [InlineData("D101", "D101A", "D101", ThreeWay.RevitChange)]
        [InlineData("D101", "D101A", "D101A", ThreeWay.SameChange)]
        [InlineData("D101", "D101A", "D101B", ThreeWay.Conflict)] // spec example: must never be overwritten silently
        [InlineData("", "", "value", ThreeWay.ExcelChange)]       // blank to value
        [InlineData("value", "value", "", ThreeWay.ExcelChange)]  // value to blank
        public void Text_three_way(string a, string b, string c, ThreeWay expected) =>
            Assert.Equal(expected, ValueComparer.Classify(CellKind.Text, a, b, c));

        [Fact]
        public void Text_comparison_is_case_sensitive() =>
            Assert.Equal(ThreeWay.ExcelChange, ValueComparer.Classify(CellKind.Text, "d01", "d01", "D01"));

        [Fact]
        public void Numbers_equal_within_unit_round_trip_tolerance()
        {
            Assert.True(ValueComparer.AreEqual(CellKind.Number, "914.4", "914.4000000000001"));
            Assert.Equal(ThreeWay.NoChange, ValueComparer.Classify(CellKind.Number, "914.4", "914.40000000000009", "914.4"));
        }

        [Fact]
        public void Numbers_that_really_differ_are_changes() =>
            Assert.Equal(ThreeWay.ExcelChange, ValueComparer.Classify(CellKind.Number, "900", "900", "900.5"));

        [Fact]
        public void Blank_number_differs_from_zero() =>
            Assert.False(ValueComparer.AreEqual(CellKind.Number, "", "0"));

        [Fact]
        public void Reference_names_ignore_surrounding_spaces() =>
            Assert.True(ValueComparer.AreEqual(CellKind.Reference, "Level 1", " Level 1 "));
    }

    public class FingerprintComparerTests
    {
        private static DocumentFingerprint Fp(string? creation = "c1", string? projectInfo = "p1", string title = "A.rvt",
            string? central = null) =>
            new() { CreationGuid = creation, ProjectInfoUniqueId = projectInfo, Title = title, CentralGuid = central };

        [Fact]
        public void Same_identifiers_match() =>
            Assert.Equal(FingerprintMatch.Match, FingerprintComparer.Compare(Fp(), Fp()).Match);

        [Fact]
        public void Renamed_copy_of_same_project_matches()
        {
            var result = FingerprintComparer.Compare(Fp(title: "A.rvt"), Fp(title: "A - copy.rvt"));
            Assert.Equal(FingerprintMatch.Match, result.Match);
            Assert.Contains(result.Reasons, r => r.Contains("A - copy.rvt"));
        }

        [Fact]
        public void Different_creation_id_blocks() =>
            Assert.Equal(FingerprintMatch.Mismatch, FingerprintComparer.Compare(Fp(creation: "c1"), Fp(creation: "c2")).Match);

        [Fact]
        public void Different_project_info_blocks() =>
            Assert.Equal(FingerprintMatch.Mismatch, FingerprintComparer.Compare(Fp(projectInfo: "p1"), Fp(projectInfo: "p2")).Match);

        [Fact]
        public void Different_central_model_blocks() =>
            Assert.Equal(FingerprintMatch.Mismatch, FingerprintComparer.Compare(Fp(central: "g1"), Fp(central: "g2")).Match);

        [Fact]
        public void Same_file_name_alone_is_never_enough() =>
            Assert.Equal(FingerprintMatch.Mismatch,
                FingerprintComparer.Compare(Fp(creation: "c1", title: "Same.rvt"), Fp(creation: "c2", title: "Same.rvt")).Match);

        [Fact]
        public void Too_few_identifiers_is_only_probable() =>
            Assert.Equal(FingerprintMatch.Probable,
                FingerprintComparer.Compare(Fp(projectInfo: null), Fp(projectInfo: null)).Match);
    }
}
