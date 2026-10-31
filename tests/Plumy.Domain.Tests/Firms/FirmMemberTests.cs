using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Domain.Tests.Firms;

public class FirmMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly EmailAddress OwnerEmail = EmailAddress.Create("ana@gestorialopez.es");
    private static readonly EmailAddress MemberEmail = EmailAddress.Create("luis@gestorialopez.es");

    private readonly Firm _firm = Firm.Create("Gestoría López", Guid.NewGuid(), DateTimeOffset.UtcNow, new UserProfile("Ana López", OwnerEmail));

    private FirmUser AddMember(string? name = "Luis Pérez")
    {
        var invitation = _firm.Invite(MemberEmail, _firm.Users.Single().ObjectId, Now);
        return _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), MemberEmail, Now, name);
    }

    [Fact]
    public void Create_KeepsTheOwnersProfile()
    {
        var owner = _firm.Users.Single();

        Assert.Equal("Ana López", owner.Name);
        Assert.Equal(OwnerEmail, owner.Email);
    }

    [Fact]
    public void Create_WithoutProfile_LeavesItEmpty()
    {
        var owner = Firm.Create("Gestoría López", Guid.NewGuid(), DateTimeOffset.UtcNow).Users.Single();

        Assert.Null(owner.Name);
        Assert.Null(owner.Email);
    }

    [Fact]
    public void AcceptInvitation_TakesTheNameFromTheAccountAndTheEmailFromTheInvitation()
    {
        var member = AddMember();

        Assert.Equal("Luis Pérez", member.Name);
        Assert.Equal(MemberEmail, member.Email);
    }

    [Fact]
    public void UpdateProfile_ChangesItOnlyWhenDifferent()
    {
        var owner = _firm.Users.Single();

        Assert.False(owner.UpdateProfile(new UserProfile(" Ana López ", OwnerEmail)));
        Assert.True(owner.UpdateProfile(new UserProfile("Ana López García", OwnerEmail)));
        Assert.Equal("Ana López García", owner.Name);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Ana  ", "Ana")]
    public void UserProfile_TidiesUpTheName(string? name, string? expected)
    {
        Assert.Equal(expected, new UserProfile(name, null).Name);
    }

    [Fact]
    public void UserProfile_CutsTooLongNamesInsteadOfFailing()
    {
        var name = new string('a', UserProfile.MaxNameLength + 10);

        Assert.Equal(UserProfile.MaxNameLength, new UserProfile(name, null).Name!.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not an email")]
    public void UserProfile_FromClaims_IgnoresAMissingOrInvalidEmail(string? email)
    {
        Assert.Null(UserProfile.FromClaims("Ana", email).Email);
    }

    [Fact]
    public void UserProfile_FromClaims_NormalizesTheEmail()
    {
        Assert.Equal(OwnerEmail, UserProfile.FromClaims("Ana", "ANA@GestoriaLopez.es").Email);
    }

    [Fact]
    public void RemoveMember_TakesThemOutOfTheFirm()
    {
        var member = AddMember();

        Assert.True(_firm.RemoveMember(member.Id));

        Assert.DoesNotContain(member, _firm.Users);
    }

    [Fact]
    public void RemoveMember_Unknown_ReturnsFalse()
    {
        Assert.False(_firm.RemoveMember(Guid.NewGuid()));
        Assert.Single(_firm.Users);
    }

    [Fact]
    public void RemoveMember_TheOwner_Throws()
    {
        Assert.Throws<DomainException>(() => _firm.RemoveMember(_firm.Users.Single().Id));
        Assert.Single(_firm.Users);
    }
}
