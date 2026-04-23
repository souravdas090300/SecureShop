using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace SecureShop.API.Pages;

public class ContactModel : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Name is required")]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Subject is required")]
    public string Subject { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Message is required")]
    [MinLength(10, ErrorMessage = "Message must be at least 10 characters")]
    public string Message { get; set; } = string.Empty;

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please fill in all required fields correctly.";
            return Page();
        }

        try
        {
            // TODO: Implement email sending logic (e.g., via SendGrid, SMTP)
            // For now, just log it
            Console.WriteLine($"Contact Form Submission:");
            Console.WriteLine($"Name: {Name}");
            Console.WriteLine($"Email: {Email}");
            Console.WriteLine($"Subject: {Subject}");
            Console.WriteLine($"Message: {Message}");

            SuccessMessage = "Thank you for contacting us! We'll get back to you within 24 hours.";
            
            // Clear form
            Name = Email = Subject = Message = string.Empty;
            ModelState.Clear();
            
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = "An error occurred while sending your message. Please try again later.";
            Console.WriteLine($"Error sending contact form: {ex.Message}");
            return Page();
        }
    }
}
