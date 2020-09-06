using System.Collections.Generic;
using Hpmv.Save;

namespace Hpmv {
    public interface IAnalyzer {
        List<AnalysisRow> Analyze(GameEntityRecords records, int lastFrame);
    }
}
