using JobPilot.Application.Abstractions;
using JobPilot.Application.Common;

namespace JobPilot.Infrastructure.Ats.Workday;

public class WorkdayAdapter : IAtsAdapter
{
    public string AtsKey => "workday";

    public bool CanHandle(Uri url)
    {
        var classification = WorkdayUrlParser.Classify(url.ToString());
        return classification.IsSupported && classification.Ats == "workday";
    }
}
