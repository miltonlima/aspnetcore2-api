using System.Globalization;
using Npgsql;

// Inicializa o host e carrega configurações/serviços básicos do ASP.NET Core.
var builder = WebApplication.CreateBuilder(args);

// Supabase Postgres connection string (set SUPABASE_DB_URL or ConnectionStrings:Supabase).
var supabaseConnString = builder.Configuration["SUPABASE_DB_URL"]
    ?? builder.Configuration.GetConnectionString("Supabase");

if (string.IsNullOrWhiteSpace(supabaseConnString))
{
    throw new InvalidOperationException("Defina SUPABASE_DB_URL ou ConnectionStrings:Supabase para conectar ao banco.");
}

builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(supabaseConnString));

// Add services to the container.
// Define política de CORS para permitir que o frontend Vite consuma a API localmente.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173",
                "https://reactvite2-app.pages.dev",
                "https://aspnetcore2-api.onrender.com"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
// Registra geradores de documentação OpenAPI (Swagger).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Constrói o pipeline de requisições a partir dos serviços definidos.
var app = builder.Build();

var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();

// Aplica CORS antes de demais middlewares para garantir cabeçalhos em erros.
app.UseCors("AllowFrontend");

// Configure the HTTP request pipeline.
// Publica o JSON OpenAPI e a interface do Swagger UI.
app.UseSwagger();
app.UseSwaggerUI();

// Mantém HTTP em desenvolvimento para evitar redirecionamentos que quebram CORS nos testes locais.
//app.UseHttpsRedirection();

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

// OBS: CRUD de instrumentos agora consulta o Postgres do Supabase.

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
app.MapPost("/validarpessoa", ([Microsoft.AspNetCore.Mvc.FromBody] PersonSubmission submission) =>
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

// CRUD minimalista de instrumentos via API REST.
app.MapGet("/api/instruments", async () =>
{
    try
    {
        var items = new List<Instrument>();
        await using var cmd = dataSource.CreateCommand("select id, name from instruments order by id");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new Instrument(reader.GetInt32(0), reader.GetString(1)));
        }
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/instruments: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListInstruments");

app.MapPost("/api/instruments", async (InstrumentCreate payload) =>
{
    // Valida entrada, insere no Postgres/Supabase e devolve 201 Created com o recurso.
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest(new { mensagem = "Nome é obrigatório." });
    }

    try
    {
        var name = payload.Name.Trim();
        await using var cmd = dataSource.CreateCommand("insert into instruments(name) values (@name) returning id, name");
        cmd.Parameters.AddWithValue("@name", name);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao inserir instrumento.");
        }
        var created = new Instrument(reader.GetInt32(0), reader.GetString(1));
        return Results.Created($"/api/instruments/{created.Id}", created);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/instruments: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateInstrument");

app.MapPut("/api/instruments/{id:int}", async (int id, InstrumentCreate payload) =>
{
    // Atualiza um instrumento existente no Postgres (valida nome, exige id existente e retorna 200 com o objeto atualizado).
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest(new { mensagem = "Nome é obrigatório." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand("update instruments set name = @name where id = @id returning id, name");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@name", payload.Name.Trim());
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound();
        }

        var updated = new Instrument(reader.GetInt32(0), reader.GetString(1));
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro PUT /api/instruments/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpdateInstrument");

app.MapDelete("/api/instruments/{id:int}", async (int id) =>
{
    try
    {
        await using var cmd = dataSource.CreateCommand("delete from instruments where id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/instruments/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("DeleteInstrument");

// Autenticação básica: busca usuário no Supabase (tabela public.users) e compara senha.
// OBS: Esta comparação é direta; se armazenar hash (recomendado), adapte para verificar o hash (ex.: BCrypt).
app.MapPost("/login", async (LoginRequest? login) =>
{
    if (login is null || string.IsNullOrWhiteSpace(login.Email) || string.IsNullOrWhiteSpace(login.Password))
    {
        return Results.BadRequest(new { mensagem = "E-mail e senha são obrigatórios." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand("select id, full_name, birth_date, sex, email, password_hash from public.users where email = @email limit 1");
        cmd.Parameters.AddWithValue("@email", login.Email.Trim());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Unauthorized();
        }

        var dbPassword = reader.GetString(reader.GetOrdinal("password_hash"));

        // Comparação direta; ajuste para a função de hash utilizada na sua base.
        var senhaValida = string.Equals(dbPassword, login.Password, StringComparison.Ordinal);

        if (!senhaValida)
        {
            return Results.Unauthorized();
        }

        var user = new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            full_name = reader.GetString(reader.GetOrdinal("full_name")),
            birth_date = reader.GetDateTime(reader.GetOrdinal("birth_date")),
            sex = reader.GetString(reader.GetOrdinal("sex")),
            email = reader.GetString(reader.GetOrdinal("email"))
        };

        return Results.Ok(new { mensagem = "Login válido.", usuario = user });
    }
    catch (PostgresException ex)
    {
        Console.WriteLine($"Erro Postgres /login: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Erro no banco", statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro /login: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("Login");


// Inicia o servidor web e bloqueia o thread principal.
app.Run();

// Record imutável que guarda data, temperatura em Celsius e descrição; a propriedade calculada converte o valor para Fahrenheit usando a fórmula tradicional.
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
record PersonSubmission(string Name, string BirthDate, string Email);
record LoginRequest(string Email, string Password);
record Instrument(int Id, string Name);
record InstrumentCreate(string Name);




