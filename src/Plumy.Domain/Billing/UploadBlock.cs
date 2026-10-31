namespace Plumy.Domain.Billing;

/// <summary>Why a firm can't upload another invoice right now.</summary>
public enum UploadBlock
{
    None,
    TrialEnded,
    LimitReached,
    NotSubscribed
}
