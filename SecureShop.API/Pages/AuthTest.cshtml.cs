using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace SecureShop.API.Pages;

public class AuthTestModel : PageModel
{
    private readonly IWebHostEnvironment _environment;

    public AuthTestModel(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public IActionResult OnGet() =>
        _environment.IsDevelopment() ? Page() : NotFound();
}
