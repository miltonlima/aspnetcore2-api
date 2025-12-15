// Identificador reutilizável para a política de CORS liberando o frontend Vite.
const string FrontendCorsPolicy = "AllowFrontend";

// Cria o builder padrão do ASP.NET Core carregando configuração e DI.
var builder = WebApplication.CreateBuilder(args);

// Configura CORS permitindo chamadas do frontend hospedado em http/https://localhost:5173.
builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()));

// Registra infraestrutura para expor documentação Swagger/OpenAPI.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Constrói o pipeline a partir dos serviços configurados.
var app = builder.Build();

// Publica JSON e UI do Swagger para inspecionar a API.
app.UseSwagger();
app.UseSwaggerUI();
// Redireciona requisições HTTP para HTTPS quando suportado.
app.UseHttpsRedirection();
// Aplica a política de CORS configurada anteriormente.
app.UseCors(FrontendCorsPolicy);

// Lista de descrições climáticas que alimenta o endpoint de forecast.
var summaries = new[]
{
    "Congelante", "Revigorante", "Frio", "Ameno", "Quente", "Agradável", "Calor", "Escalante", "Torrente", "Abrasador"
};

// Minimal API que gera cinco previsões com datas futuras e dados aleatórios.
app.MapGet("/weatherforecast", () =>
    Enumerable.Range(1, 5)
        .Select(day => new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(day)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]))
        .ToArray())
.WithName("GetWeatherForecast")
.WithOpenApi();

// Endpoint que sorteia seis números únicos entre 1 e 60 para uma loteria simples.
app.MapGet("/lottery", () =>
    Random.Shared
        .GetItems(Enumerable.Range(1, 60).ToArray(), 6)
        .Order()
        .ToArray())
.WithName("GetLotteryNumbers")
.WithOpenApi();

// Inicia o servidor web e bloqueia até que seja interrompido.
app.Run();

// Record que representa cada previsão climática incluindo conversão para Fahrenheit.
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => (int)Math.Round(TemperatureC * 9 / 5.0 + 32);
}




