using System.Globalization;

// Inicializa o host e carrega configurações/serviços básicos do ASP.NET Core.
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Define política de CORS para permitir que o frontend Vite consuma a API localmente.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173", "https://reactvite2-app.pages.dev")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// Registra geradores de documentação OpenAPI (Swagger).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Constrói o pipeline de requisições a partir dos serviços definidos.
var app = builder.Build();

// Configure the HTTP request pipeline.
// Publica o JSON OpenAPI e a interface do Swagger UI.
app.UseSwagger();
app.UseSwaggerUI();

// Força redirecionamento para HTTPS quando disponível.
app.UseHttpsRedirection();

// Aplica a política de CORS nomeada para liberar chamadas do frontend.
app.UseCors("AllowFrontend");

// Endpoint de teste simples que retorna uma string.
app.MapGet("/texto", () => "Vim da API!").WithName("Texto");

// Endpoint que recebe dois valores e informa qual deles é maior.
app.MapGet("/comparar", (int primeiro, int segundo) =>
{
    if (primeiro == segundo)
    {
        return Results.Ok(new
        {
            mensagem = "Os dois números são iguais.",
            maior = primeiro,
            valores = new[] { primeiro, segundo }
        });
    }

    var maior = Math.Max(primeiro, segundo);
    var menor = Math.Min(primeiro, segundo);

    return Results.Ok(new
    {
        mensagem = $"O maior número é {maior}.",
        maior,
        menor
    });
}).WithName("Comparar");

// Endpoint de teste simples que retorna uma soma.
app.MapGet("/soma", () => 2 + 3).WithName("Soma");

var summaries = new[]
{
    "Congelante", "Revigorante", "Frio", "Ameno", "Quente", "Agradável", "Calor", "Escalante", "Torrente", "Abrasador"
};

// Lista simples de 5 e-mails para comparação com o front-end
var allowedEmails = new[]
{
    "teste1@example.com",
    "usuario2@example.com",
    "cliente3@example.com",
    "contato4@example.com",
    "admin5@example.com"
};

// Endpoint mínimo que devolve 3 registros de previsão climática com valores aleatórios.
app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 3).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Endpoint que gera 6 números aleatórios entre 1 e 60 sem repetição.
app.MapGet("/lottery", () =>
{
    var numbers = new HashSet<int>();
    while (numbers.Count < 6)
    {
        // Adiciona ao conjunto um número aleatório entre 1 e 60, evitando duplicatas.
        numbers.Add(Random.Shared.Next(1, 61));
    }
    return numbers
        .OrderBy(value => value)
        .ToArray();
})
.WithName("GetLotteryNumbers");
// Endpoint que recebe nome, data de nascimento e e-mail do formulário (App8.jsx),
// informa se a pessoa é maior de idade e se o e-mail está presente na lista de 5 e-mails.
app.MapPost("/validar-pessoa", ([Microsoft.AspNetCore.Mvc.FromBody] PersonSubmission submission) =>
{
    try
    {
        if (submission == null || string.IsNullOrWhiteSpace(submission.Name) ||
            string.IsNullOrWhiteSpace(submission.BirthDate) ||
            string.IsNullOrWhiteSpace(submission.Email))
        {
            return Results.BadRequest(new { mensagem = "Todos os campos (Name, BirthDate, Email) são obrigatórios." });
        }

        // Parse flexível da data enviada pelo frontend (input type=date envia yyyy-MM-dd)
        DateTime birthDate;
        if (!DateTime.TryParse(submission.BirthDate, out birthDate))
        {
            // Tenta formatos comuns: dd/MM/yyyy (frontend formatado) ou yyyy-MM-dd (ISO)
            if (!DateTime.TryParseExact(submission.BirthDate,
                                        new[] { "dd/MM/yyyy", "yyyy-MM-dd", "dd-MM-yyyy" },
                                        CultureInfo.InvariantCulture,
                                        DateTimeStyles.None,
                                        out birthDate))
            {
                return Results.BadRequest(new { mensagem = "Formato de data inválido. Use dd/MM/yyyy ou yyyy-MM-dd." });
            }
        }

        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;

        var isAdult = age >= 18;

        var emailExists = allowedEmails.Any(e => string.Equals(e, submission.Email, StringComparison.OrdinalIgnoreCase));

        return Results.Ok(new
        {
            mensagem = "Validação concluída.",
            nome = submission.Name,
            idade = age,
            maiorDeIdade = isAdult,
            emailExistente = emailExists
        });
    }
    catch (Exception ex)
    {
        // Retorna detalhes da exceção em JSON para facilitar debug
        System.Console.WriteLine($"Erro em /validar-pessoa: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: $"Erro: {ex.Message}", title: "Internal Server Error", statusCode: 500);
    }
})
.WithName("ValidarPessoa");




// Inicia o servidor web e bloqueia o thread principal.
app.Run();

// Record imutável que guarda data, temperatura em Celsius e descrição; a propriedade calculada converte o valor para Fahrenheit usando a fórmula tradicional.
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
record PersonSubmission(string Name, string BirthDate, string Email);




