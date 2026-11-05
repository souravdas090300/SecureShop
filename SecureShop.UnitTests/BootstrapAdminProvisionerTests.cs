using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecureShop.API.Helpers;
using SecureShop.Domain.Entities;

namespace SecureShop.UnitTests;

public class BootstrapAdminProvisionerTests
{
    [Fact]
    public void GetCredentials_WhenUnset_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Null(BootstrapAdminProvisioner.GetCredentials(configuration));
    }

    [Fact]
    public void GetCredentials_WhenOnlyOneValueIsSet_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = "admin@example.com"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => BootstrapAdminProvisioner.GetCredentials(configuration));
    }

    [Fact]
    public async Task ProvisionIfNeededAsync_WhenUnconfigured_DoesNotCallUserManager()
    {
        var userManager = CreateUserManager();

        await BootstrapAdminProvisioner.ProvisionIfNeededAsync(
            userManager.Object,
            null,
            NullLogger.Instance);

        userManager.Verify(manager => manager.GetUsersInRoleAsync(It.IsAny<string>()), Times.Never);
        userManager.Verify(manager => manager.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        userManager.Verify(manager => manager.CreateAsync(
            It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionIfNeededAsync_WhenAdminExists_SkipsBootstrap()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser> { new() { Id = "existing-admin" } });

        await BootstrapAdminProvisioner.ProvisionIfNeededAsync(
            userManager.Object,
            new BootstrapAdminCredentials("admin@example.com", "StrongPassword123!"),
            NullLogger.Instance);

        userManager.Verify(manager => manager.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        userManager.Verify(manager => manager.CreateAsync(
            It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionIfNeededAsync_WhenConfigured_CreatesAdminAndAssignsRole()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());
        userManager.Setup(manager => manager.FindByEmailAsync("admin@example.com"))
            .ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(manager => manager.CreateAsync(
                It.IsAny<ApplicationUser>(), "StrongPassword123!"))
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Success);

        await BootstrapAdminProvisioner.ProvisionIfNeededAsync(
            userManager.Object,
            new BootstrapAdminCredentials("admin@example.com", "StrongPassword123!"),
            NullLogger.Instance);

        userManager.Verify(manager => manager.CreateAsync(
            It.Is<ApplicationUser>(user =>
                user.Email == "admin@example.com" &&
                user.UserName == "admin@example.com" &&
                user.EmailConfirmed),
            "StrongPassword123!"), Times.Once);
        userManager.Verify(manager => manager.AddToRoleAsync(
            It.Is<ApplicationUser>(user => user.Email == "admin@example.com"),
            "Admin"), Times.Once);
    }

    [Fact]
    public async Task ProvisionIfNeededAsync_WhenConfiguredEmailBelongsToNonAdmin_ThrowsWithoutPromoting()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());
        userManager.Setup(manager => manager.FindByEmailAsync("admin@example.com"))
            .ReturnsAsync(new ApplicationUser { Email = "admin@example.com" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BootstrapAdminProvisioner.ProvisionIfNeededAsync(
                userManager.Object,
                new BootstrapAdminCredentials("admin@example.com", "StrongPassword123!"),
                NullLogger.Instance));

        userManager.Verify(manager => manager.AddToRoleAsync(
            It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionIfNeededAsync_WhenUserCreationFails_Throws()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());
        userManager.Setup(manager => manager.FindByEmailAsync("admin@example.com"))
            .ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(manager => manager.CreateAsync(
                It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Password rejected" }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BootstrapAdminProvisioner.ProvisionIfNeededAsync(
                userManager.Object,
                new BootstrapAdminCredentials("admin@example.com", "weak"),
                NullLogger.Instance));

        userManager.Verify(manager => manager.AddToRoleAsync(
            It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionIfNeededAsync_WhenRoleAssignmentFails_RemovesCreatedUserAndThrows()
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());
        userManager.Setup(manager => manager.FindByEmailAsync("admin@example.com"))
            .ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(manager => manager.CreateAsync(
                It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        userManager.Setup(manager => manager.AddToRoleAsync(
                It.IsAny<ApplicationUser>(), "Admin"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role assignment failed" }));
        userManager.Setup(manager => manager.DeleteAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BootstrapAdminProvisioner.ProvisionIfNeededAsync(
                userManager.Object,
                new BootstrapAdminCredentials("admin@example.com", "StrongPassword123!"),
                NullLogger.Instance));

        Assert.Contains("account was removed", exception.Message);
        userManager.Verify(manager => manager.DeleteAsync(
            It.Is<ApplicationUser>(user => user.Email == "admin@example.com")), Times.Once);
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
