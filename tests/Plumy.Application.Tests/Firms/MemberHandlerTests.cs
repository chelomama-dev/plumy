using Plumy.Application.Firms.GetCurrentUser;
using Plumy.Application.Firms.ListMembers;
using Plumy.Application.Firms.RemoveMember;
using Plumy.Domain.Common;
using Plumy.Domain.Firms;

namespace Plumy.Application.Tests.Firms;

public class MemberHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryFirmStore _store = new();
    private readonly FakeCurrentUser _owner = new();
    private readonly Firm _firm;

    public MemberHandlerTests() =>
        _firm = _store.Seed("Gestoría López", _owner.ObjectId, UserProfile.FromClaims(_owner.Name, _owner.Email));

    private FirmUser AddMember(string email, string? name)
    {
        var address = EmailAddress.Create(email);
        var invitation = _firm.Invite(address, _owner.ObjectId, Now);
        return _firm.AcceptInvitation(invitation.Id, Guid.NewGuid(), address, Now, name);
    }

    private ListMembersHandler List(FakeCurrentUser user) => new(new FakeTenant(_firm.Id), user, _store);

    private RemoveMemberHandler Remove() => new(new FakeTenant(_firm.Id), _store, _store);

    private GetCurrentUserHandler Me(FakeCurrentUser user) => new(user, _store, _store, TimeProvider.System);

    [Fact]
    public async Task List_ShowsTheOwnerFirst_ThenByName_WithoutNameLast()
    {
        AddMember("zoe@example.com", "Zoe");
        AddMember("sin.nombre@example.com", null);
        AddMember("alvaro@example.com", "Álvaro");
        AddMember("bea@example.com", "Bea");

        var members = await List(_owner).HandleAsync();

        Assert.Equal(
            ["Ana López", "Álvaro", "Bea", "Zoe", null],
            members.Select(m => m.Name));
        Assert.Equal(["Owner", "Member", "Member", "Member", "Member"], members.Select(m => m.Role));
        Assert.Equal("sin.nombre@example.com", members[^1].Email);
    }

    [Fact]
    public async Task List_MarksTheCurrentUser()
    {
        var member = AddMember("luis@example.com", "Luis");

        var asMember = await List(new FakeCurrentUser { ObjectId = member.ObjectId }).HandleAsync();

        Assert.Equal([false, true], asMember.Select(m => m.IsCurrentUser));
    }

    [Fact]
    public async Task Remove_Member_TakesThemOutAndSaves()
    {
        var member = AddMember("luis@example.com", "Luis");

        Assert.True(await Remove().HandleAsync(member.Id));

        Assert.DoesNotContain(member, _firm.Users);
        Assert.Equal(1, _store.SaveCount);
        Assert.Null(await _store.GetMembershipAsync(member.ObjectId));
    }

    [Fact]
    public async Task Remove_Unknown_ReturnsFalseWithoutSaving()
    {
        Assert.False(await Remove().HandleAsync(Guid.NewGuid()));
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Remove_TheOwner_ThrowsWithoutSaving()
    {
        await Assert.ThrowsAsync<DomainException>(() => Remove().HandleAsync(_firm.Users.Single().Id));
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Me_UpdatesTheStoredProfile_WhenTheAccountChanged()
    {
        _owner.Name = "Ana López García";

        await Me(_owner).HandleAsync();

        Assert.Equal("Ana López García", _firm.Users.Single().Name);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Me_DoesNotSave_WhenTheProfileIsUnchanged()
    {
        await Me(_owner).HandleAsync();

        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Me_FillsInTheProfileOfUsersWhoJoinedBeforeItWasStored()
    {
        var firm = _store.Seed("Gestoría antigua", Guid.NewGuid());
        var user = new FakeCurrentUser { ObjectId = firm.Users.Single().ObjectId, Email = "PEPE@example.com" };

        await Me(user).HandleAsync();

        Assert.Equal("Ana López", firm.Users.Single().Name);
        Assert.Equal("pepe@example.com", firm.Users.Single().Email?.Value);
    }
}
