using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace RealEstateApp.WebApp.ViewModels;

public sealed class LoginInputViewModel
{
    [Required(ErrorMessage = "Ingrese su correo o nombre de usuario."), Display(Name = "Correo o nombre de usuario")]
    public string Login { get; set; } = "";
    [Required(ErrorMessage = "Ingrese su contraseña."), DataType(DataType.Password), Display(Name = "Contraseña")]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterInputViewModel
{
    [Required(ErrorMessage = "El nombre es requerido."), StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres."), Display(Name = "Nombre")]
    public string FirstName { get; set; } = "";
    [Required(ErrorMessage = "El apellido es requerido."), StringLength(100, ErrorMessage = "El apellido admite hasta 100 caracteres."), Display(Name = "Apellido")]
    public string LastName { get; set; } = "";
    [Required(ErrorMessage = "El teléfono es requerido."), Phone(ErrorMessage = "Ingrese un teléfono válido."), Display(Name = "Teléfono")]
    public string Phone { get; set; } = "";
    [Required(ErrorMessage = "La foto es requerida."), ImageUpload, Display(Name = "Foto de usuario")]
    public IFormFile? Photo { get; set; }
    [Required(ErrorMessage = "El nombre de usuario es requerido."), StringLength(100, ErrorMessage = "El usuario admite hasta 100 caracteres."), Display(Name = "Nombre de usuario")]
    public string UserName { get; set; } = "";
    [Required(ErrorMessage = "El correo es requerido."), EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido."), Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "La contraseña es requerida."), StringLength(128, MinimumLength = 8, ErrorMessage = "Use entre 8 y 128 caracteres."), DataType(DataType.Password), Display(Name = "Contraseña")]
    public string Password { get; set; } = "";
    [Required(ErrorMessage = "Confirme la contraseña."), Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden."), DataType(DataType.Password), Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = "";
    [Required(ErrorMessage = "Seleccione el tipo de usuario."), RegularExpression("^(Cliente|Agente)$", ErrorMessage = "Solo puede registrarse como Cliente o Agente."), Display(Name = "Tipo de usuario")]
    public string Role { get; set; } = "Cliente";
}

public sealed class ProfileInputViewModel
{
    [Required(ErrorMessage = "El nombre es requerido."), StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres."), Display(Name = "Nombre")]
    public string FirstName { get; set; } = "";
    [Required(ErrorMessage = "El apellido es requerido."), StringLength(100, ErrorMessage = "El apellido admite hasta 100 caracteres."), Display(Name = "Apellido")]
    public string LastName { get; set; } = "";
    [Required(ErrorMessage = "El teléfono es requerido."), Phone(ErrorMessage = "Ingrese un teléfono válido."), Display(Name = "Teléfono")]
    public string Phone { get; set; } = "";
    [ImageUpload, Display(Name = "Nueva foto (opcional)")] public IFormFile? Photo { get; set; }
    [BindNever, ValidateNever] public string? CurrentPhoto { get; set; }
}

public sealed class UserViewModel
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
    public string UserName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Photo { get; set; }
    public string? NationalId { get; set; }
    public bool IsActive { get; set; }
    public string Role { get; set; } = "";
    public int PropertyCount { get; set; }
}

public sealed class AgentSearchViewModel
{
    [StringLength(100, ErrorMessage = "La búsqueda admite hasta 100 caracteres."), Display(Name = "Nombre o apellido del agente")]
    public string? Name { get; set; }
    [BindNever, ValidateNever] public List<UserViewModel> Agents { get; set; } = [];
}

public class AdminUserInputViewModel : IValidatableObject
{
    [Required(ErrorMessage = "El nombre es requerido."), StringLength(100, ErrorMessage = "El nombre admite hasta 100 caracteres."), Display(Name = "Nombre")]
    public string FirstName { get; set; } = "";
    [Required(ErrorMessage = "El apellido es requerido."), StringLength(100, ErrorMessage = "El apellido admite hasta 100 caracteres."), Display(Name = "Apellido")]
    public string LastName { get; set; } = "";
    [Required(ErrorMessage = "La cédula es requerida."), StringLength(30, ErrorMessage = "La cédula admite hasta 30 caracteres."), Display(Name = "Cédula")]
    public string NationalId { get; set; } = "";
    [Required(ErrorMessage = "El correo es requerido."), EmailAddress(ErrorMessage = "Ingrese un correo electrónico válido."), Display(Name = "Correo electrónico")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "El usuario es requerido."), StringLength(100, ErrorMessage = "El usuario admite hasta 100 caracteres."), Display(Name = "Nombre de usuario")]
    public string UserName { get; set; } = "";
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Use entre 8 y 128 caracteres."), DataType(DataType.Password), Display(Name = "Contraseña")]
    public string? Password { get; set; }
    [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden."), DataType(DataType.Password), Display(Name = "Confirmar contraseña")]
    public string? ConfirmPassword { get; set; }
    [BindNever, ValidateNever] public string Role { get; set; } = "";
    [BindNever, ValidateNever] public string? Id { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (this is AdminUserCreateInputViewModel && string.IsNullOrWhiteSpace(Password))
            yield return new("La contraseña es requerida.", [nameof(Password)]);
        if ((this is AdminUserCreateInputViewModel || Password is not null) && string.IsNullOrWhiteSpace(ConfirmPassword))
            yield return new("Confirme la nueva contraseña.", [nameof(ConfirmPassword)]);
    }
}

public sealed class AdminUserCreateInputViewModel : AdminUserInputViewModel;

public sealed class StatusInputViewModel
{
    [Required(ErrorMessage = "Indique el nuevo estado.")] public bool? Active { get; set; }
}

public sealed class UserListingViewModel
{
    public string Role { get; set; } = "";
    public List<UserViewModel> Users { get; set; } = [];
}
