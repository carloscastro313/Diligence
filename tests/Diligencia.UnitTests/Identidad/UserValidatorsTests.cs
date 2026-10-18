using Diligencia.Modules.Identidad.Contracts;
using Diligencia.Modules.Identidad.Features.CreateArea;
using Diligencia.Modules.Identidad.Features.CreateUser;
using Diligencia.Modules.Identidad.Features.ResetPassword;

namespace Diligencia.UnitTests.Identidad;

public class UserValidatorsTests
{
    private static readonly UserRequestValidator Users = new();
    private static UserRequest Valid => new("Ana", "ana@test.local", Guid.NewGuid(), [BackofficeRoles.Analista]);

    [Fact]
    public void ValidRequest_Passes() => Assert.True(Users.Validate(Valid).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsRequired(string? name) => Assert.False(Users.Validate(Valid with { Name = name }).IsValid);

    [Fact]
    public void Name_AndEmail_HaveMaxLengths()
    {
        Assert.False(Users.Validate(Valid with { Name = new string('a', 201) }).IsValid);
        Assert.False(Users.Validate(Valid with { Email = new string('a', 250) + "@x.co" }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin-arroba")]
    public void Email_MustBeAnAddress(string? email) => Assert.False(Users.Validate(Valid with { Email = email }).IsValid);

    [Fact]
    public void Area_IsRequired()
    {
        Assert.False(Users.Validate(Valid with { AreaId = null }).IsValid);
        Assert.False(Users.Validate(Valid with { AreaId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Roles_NeedAtLeastOne_BeValid_AndNotRepeat()
    {
        Assert.False(Users.Validate(Valid with { Roles = null }).IsValid);
        Assert.False(Users.Validate(Valid with { Roles = [] }).IsValid);
        Assert.False(Users.Validate(Valid with { Roles = ["Dios"] }).IsValid);
        Assert.False(Users.Validate(Valid with { Roles = [BackofficeRoles.Auditor, BackofficeRoles.Auditor] }).IsValid);
        Assert.True(Users.Validate(Valid with { Roles = [BackofficeRoles.Auditor, BackofficeRoles.Solicitante] }).IsValid);
    }

    [Fact]
    public void AreaName_IsRequiredAndAtMost100()
    {
        var areas = new AreaRequestValidator();
        Assert.False(areas.Validate(new AreaRequest(" ")).IsValid);
        Assert.False(areas.Validate(new AreaRequest(new string('a', 101))).IsValid);
        Assert.True(areas.Validate(new AreaRequest("Compras")).IsValid);
    }

    [Fact]
    public void ResetPassword_RequiresTokenAndAValidPassword()
    {
        var reset = new ResetPasswordRequestValidator();
        Assert.True(reset.Validate(new ResetPasswordRequest("tok", new string('a', 12))).IsValid);
        Assert.False(reset.Validate(new ResetPasswordRequest("", new string('a', 12))).IsValid);
        Assert.False(reset.Validate(new ResetPasswordRequest("tok", new string('a', 11))).IsValid);
    }
}
