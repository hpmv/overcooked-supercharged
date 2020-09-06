using System.Collections.Generic;
using Hpmv.Save;

namespace Hpmv {
    public class MixerAnalyzer : IAnalyzer {
        public List<AnalysisRow> Analyze(GameEntityRecords records, int lastFrame) {
            var results = new List<AnalysisRow>();
            var count = 0;
            foreach (var entity in records.GenAllEntities()) {
                if (entity.className == "mixer") {
                    var row = new AnalysisRow() {
                        Name = "Mixer #" + ++count
                    };
                    var block = new AnalysisBlock();
                    block.StartFrame = 0;
                    block.EndFrame = lastFrame;
                    row.Blocks.Add(block);
                    var startFrame = 0;
                    var startProgress = 0.0;
                    var currentData = entity.data.initialValue;
                    foreach (var (time, data) in entity.data.changes) {
                        var prevCount = currentData.contents?.Count ?? 0;
                        var currentCount = data.contents?.Count ?? 0;
                        if (prevCount != currentCount) {
                            var prevProgress = entity.progress[time - 1];
                            var curProgress = entity.progress[time];
                            var range = new AnalysisRange() {
                                Id = row.Ranges.Count,
                                StartFrame = startFrame,
                                EndFrame = time,
                                StartValue = startProgress / 12,
                                EndValue = prevProgress / 12,
                                HasValue = prevCount > 0,
                                Kind = 10000 + prevCount,
                            };
                            row.Ranges.Add(range);
                            block.Ranges.Add(range.Id);

                            startFrame = time;
                            startProgress = curProgress;
                        }

                        currentData = data;
                    }
                    results.Add(row);
                }
            }
            return results;
        }
    }
}