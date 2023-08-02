using Google.Protobuf;
using Hpmv;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;

namespace controller.Pages {
    partial class EntityRecordRenderer {
        [Parameter]
        public GameEntityRecord Record { get; set; }

        [Parameter]
        public GameEntityRecords AllRecords { get; set; }

        [Parameter]
        public int Frame { get; set; }

        private bool ShowDetails { get; set; }

        public string ToJson(IMessage msg) {
            return JsonConvert.SerializeObject(JsonConvert.DeserializeObject(JsonFormatter.ToDiagnosticString(msg)), Formatting.Indented);
        }
    }
}