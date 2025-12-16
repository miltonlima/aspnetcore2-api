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
app.MapGet("/ping", () => "Vindo da API!").WithName("Ping");

var summaries = new[]
{
    "Congelante", "Revigorante", "Frio", "Ameno", "Quente", "Agradável", "Calor", "Escalante", "Torrente", "Abrasador"
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

// Inicia o servidor web e bloqueia o thread principal.
app.Run();

// Record imutável que guarda data, temperatura em Celsius e descrição; a propriedade calculada converte o valor para Fahrenheit usando a fórmula tradicional.
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}




