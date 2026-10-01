using DAP.Core.Guides;

namespace DAP.Data.Guides;

public interface IGuideStepRepository
{
    Task<IReadOnlyList<GuideStep>> GetStepsAsync(
        string guideId,
        CancellationToken cancellationToken = default);

    Task SaveStepAsync(
        string guideId,
        GuideStep step,
        CancellationToken cancellationToken = default);
}
