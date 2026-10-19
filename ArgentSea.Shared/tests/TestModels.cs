using System.Collections.Generic;
using ArgentSea.TestKit;

namespace ArgentSea.Test
{
    /// <summary>Models mapped with the TestKit's provider-neutral attributes, shared by the spec-section test files.</summary>
    public static class TestModels
    {
        /// <summary>A root model: Id is required (a null Id column or output yields a null model), with an optional list property for a second result set.</summary>
        public class Customer
        {
            [FakeMap("Id", true)]
            public int Id { get; set; }

            [FakeMap("Name")]
            public string? Name { get; set; }

            public List<OrderLine>? Lines { get; set; }

            public List<Note>? Notes { get; set; }
        }

        public class Note
        {
            [FakeMap("Text")]
            public string? Text { get; set; }
        }

        public class OrderLine
        {
            [FakeMap("LineId")]
            public int LineId { get; set; }

            [FakeMap("Qty")]
            public int Qty { get; set; }
        }

        /// <summary>A root model whose collection property is populated from the second result set by MapReaderWithCollectionsAsync.</summary>
        public class CustomerWithLines
        {
            [FakeMap("Id", true)]
            public int Id { get; set; }

            [FakeCollectionMap("Lines")]
            public List<OrderLine>? Lines { get; set; }
        }

        public class CustomerSummary
        {
            [FakeMap("Id")]
            public int Id { get; set; }
        }
    }
}
