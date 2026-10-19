using Plumy.Domain.Firms;

namespace Plumy.Application.Firms;

/// <summary>Which firm a user belongs to and with what role.</summary>
public sealed record FirmMembership(Guid FirmId, string FirmName, FirmRole Role);
