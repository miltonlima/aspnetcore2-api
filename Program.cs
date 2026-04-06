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

// Descobre colunas disponíveis em public.users para manter compatibilidade de schema.
var userColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
try
{
    await using var userColumnsCmd = dataSource.CreateCommand(@"
        select column_name
        from information_schema.columns
        where table_schema = 'public' and table_name = 'users'");
    await using var userColumnsReader = await userColumnsCmd.ExecuteReaderAsync();
    while (await userColumnsReader.ReadAsync())
    {
        userColumns.Add(userColumnsReader.GetString(0));
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Falha ao ler schema de public.users: {ex.Message}");
}

var hasUsersTable = userColumns.Count > 0;
var hasUsersIsActive = userColumns.Contains("is_active");
var hasUsersActive = userColumns.Contains("active");
var hasUsersStatus = userColumns.Contains("status");
var hasUsersInactiveAt = userColumns.Contains("inactive_at");
var hasUsersUpdatedAt = userColumns.Contains("updated_at");

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

        var passwordHashOrdinal = reader.GetOrdinal("password_hash");
        if (reader.IsDBNull(passwordHashOrdinal))
        {
            return Results.Unauthorized();
        }

        var dbPassword = reader.GetString(passwordHashOrdinal);

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

// Endpoint de cadastro: recebe dados do formulário e cria novo usuário no Supabase.
app.MapPost("/register", async (RegisterRequest? register) =>
{
    if (register is null || string.IsNullOrWhiteSpace(register.FullName) || 
        string.IsNullOrWhiteSpace(register.BirthDate) || 
        string.IsNullOrWhiteSpace(register.Sex) ||
        string.IsNullOrWhiteSpace(register.Email) || 
        string.IsNullOrWhiteSpace(register.Password))
    {
        return Results.BadRequest(new { mensagem = "Todos os campos são obrigatórios." });
    }

    if (!register.Password.Equals(register.ConfirmPassword, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { mensagem = "As senhas não conferem." });
    }

    // Valida formato de e-mail simples
    if (!register.Email.Contains("@"))
    {
        return Results.BadRequest(new { mensagem = "E-mail inválido." });
    }

    try
    {
        // Verifica se e-mail já existe
        await using var checkCmd = dataSource.CreateCommand("select id from public.users where email = @email limit 1");
        checkCmd.Parameters.AddWithValue("@email", register.Email.Trim());
        await using var checkReader = await checkCmd.ExecuteReaderAsync();
        if (await checkReader.ReadAsync())
        {
            return Results.BadRequest(new { mensagem = "E-mail já cadastrado." });
        }

        // Insere novo usuário
        var registerColumns = "full_name, birth_date, sex, email, password_hash";
        var registerValues = "@full_name, @birth_date, @sex, @email, @password_hash";
        if (hasUsersActive)
        {
            registerColumns += ", active";
            registerValues += ", @active";
        }

        await using var cmd = dataSource.CreateCommand(
            $"insert into public.users ({registerColumns}) " +
            $"values ({registerValues}) " +
            "returning id, full_name, birth_date, sex, email");
        
        cmd.Parameters.AddWithValue("@full_name", register.FullName.Trim());
        cmd.Parameters.AddWithValue("@birth_date", DateTime.Parse(register.BirthDate));
        cmd.Parameters.AddWithValue("@sex", register.Sex.Trim());
        cmd.Parameters.AddWithValue("@email", register.Email.Trim());
        cmd.Parameters.AddWithValue("@password_hash", register.Password);
        if (hasUsersActive)
        {
            cmd.Parameters.AddWithValue("@active", true);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao criar usuário.");
        }

        var user = new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            full_name = reader.GetString(reader.GetOrdinal("full_name")),
            birth_date = reader.GetDateTime(reader.GetOrdinal("birth_date")),
            sex = reader.GetString(reader.GetOrdinal("sex")),
            email = reader.GetString(reader.GetOrdinal("email"))
        };

        return Results.Created($"/register/{user.id}", new { mensagem = "Cadastro realizado com sucesso.", usuario = user });
    }
    catch (PostgresException ex)
    {
        Console.WriteLine($"Erro Postgres /register: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Erro no banco", statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro /register: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("Register");

// Lista alunos com opção de incluir inativos.
app.MapGet("/api/alunos", async (bool includeInactive = false) =>
{
    if (!hasUsersTable)
    {
        return Results.Problem(
            detail: "Tabela public.users não encontrada no banco.",
            title: "Regra de negócio indisponível",
            statusCode: 500);
    }

    try
    {
        var statusSql = hasUsersIsActive
            ? "coalesce(is_active, true)"
            : hasUsersActive
                ? "coalesce(active, true)"
                : hasUsersStatus
                    ? "(upper(coalesce(status, 'A')) in ('A', 'ATIVO', '1', 'TRUE'))"
                    : hasUsersInactiveAt
                        ? "(inactive_at is null)"
                        : "true";

        var whereSql = string.Empty;
        if (!includeInactive)
        {
            if (hasUsersIsActive)
            {
                whereSql = " where coalesce(is_active, true) = true";
            }
            else if (hasUsersActive)
            {
                whereSql = " where coalesce(active, true) = true";
            }
            else if (hasUsersStatus)
            {
                whereSql = " where upper(coalesce(status, 'A')) in ('A', 'ATIVO', '1', 'TRUE')";
            }
            else if (hasUsersInactiveAt)
            {
                whereSql = " where inactive_at is null";
            }
        }

        var sql = $@"
            select
                id,
                full_name,
                birth_date,
                sex,
                email,
                {statusSql} as is_active
            from public.users
            {whereSql}
            order by full_name";

        var items = new List<StudentListItem>();
        await using var cmd = dataSource.CreateCommand(sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new StudentListItem(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("full_name")),
                reader.GetDateTime(reader.GetOrdinal("birth_date")),
                reader.GetString(reader.GetOrdinal("sex")),
                reader.GetString(reader.GetOrdinal("email")),
                reader.GetBoolean(reader.GetOrdinal("is_active"))
            ));
        }

        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/alunos: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListAlunos");

// Retorna o cadastro detalhado de um aluno.
app.MapGet("/api/alunos/{id:long}", async (long id) =>
{
    if (!hasUsersTable)
    {
        return Results.Problem(
            detail: "Tabela public.users não encontrada no banco.",
            title: "Regra de negócio indisponível",
            statusCode: 500);
    }

    try
    {
        var statusSql = hasUsersIsActive
            ? "coalesce(is_active, true)"
            : hasUsersActive
                ? "coalesce(active, true)"
                : hasUsersStatus
                    ? "(upper(coalesce(status, 'A')) in ('A', 'ATIVO', '1', 'TRUE'))"
                    : hasUsersInactiveAt
                        ? "(inactive_at is null)"
                        : "true";
        var inactiveAtSql = hasUsersInactiveAt ? "inactive_at" : "null::timestamp";

        var sql = $@"
            select
                id,
                full_name,
                birth_date,
                sex,
                email,
                {statusSql} as is_active,
                {inactiveAtSql} as inactive_at
            from public.users
            where id = @id
            limit 1";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Aluno não encontrado." });
        }

        var inactiveAtOrdinal = reader.GetOrdinal("inactive_at");

        var aluno = new StudentDetail(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("full_name")),
            reader.GetDateTime(reader.GetOrdinal("birth_date")),
            reader.GetString(reader.GetOrdinal("sex")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(inactiveAtOrdinal) ? null : reader.GetDateTime(inactiveAtOrdinal)
        );

        return Results.Ok(aluno);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/alunos/{{id}}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("GetAlunoById");

// Atualiza dados cadastrais de um aluno.
app.MapPut("/api/alunos/{id:long}", async (long id, StudentUpdateRequest? payload) =>
{
    if (!hasUsersTable)
    {
        return Results.Problem(
            detail: "Tabela public.users não encontrada no banco.",
            title: "Regra de negócio indisponível",
            statusCode: 500);
    }

    if (payload is null ||
        string.IsNullOrWhiteSpace(payload.FullName) ||
        string.IsNullOrWhiteSpace(payload.BirthDate) ||
        string.IsNullOrWhiteSpace(payload.Sex) ||
        string.IsNullOrWhiteSpace(payload.Email))
    {
        return Results.BadRequest(new { mensagem = "Todos os campos são obrigatórios." });
    }

    if (!payload.Email.Contains("@"))
    {
        return Results.BadRequest(new { mensagem = "E-mail inválido." });
    }

    if (!DateTime.TryParse(payload.BirthDate, out var parsedBirthDate))
    {
        if (!DateTime.TryParseExact(payload.BirthDate,
                                    new[] { "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy" },
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out parsedBirthDate))
        {
            return Results.BadRequest(new { mensagem = "Formato de data inválido. Use yyyy-MM-dd ou dd/MM/yyyy." });
        }
    }

    try
    {
        await using var checkEmailCmd = dataSource.CreateCommand(@"
            select 1
            from public.users
            where lower(email) = lower(@email) and id <> @id
            limit 1");
        checkEmailCmd.Parameters.AddWithValue("@email", payload.Email.Trim());
        checkEmailCmd.Parameters.AddWithValue("@id", id);
        var duplicateEmail = await checkEmailCmd.ExecuteScalarAsync();
        if (duplicateEmail is not null)
        {
            return Results.BadRequest(new { mensagem = "Já existe um aluno com este e-mail." });
        }

        var statusSql = hasUsersIsActive
            ? "coalesce(is_active, true)"
            : hasUsersActive
                ? "coalesce(active, true)"
                : hasUsersStatus
                    ? "(upper(coalesce(status, 'A')) in ('A', 'ATIVO', '1', 'TRUE'))"
                    : hasUsersInactiveAt
                        ? "(inactive_at is null)"
                        : "true";
        var inactiveAtSql = hasUsersInactiveAt ? "inactive_at" : "null::timestamp";
        var updatedAtSetSql = hasUsersUpdatedAt ? ", updated_at = now()" : string.Empty;

        var sql = $@"
            update public.users
            set
                full_name = @full_name,
                birth_date = @birth_date,
                sex = @sex,
                email = @email
                {updatedAtSetSql}
            where id = @id
            returning
                id,
                full_name,
                birth_date,
                sex,
                email,
                {statusSql} as is_active,
                {inactiveAtSql} as inactive_at";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@full_name", payload.FullName.Trim());
        cmd.Parameters.AddWithValue("@birth_date", parsedBirthDate.Date);
        cmd.Parameters.AddWithValue("@sex", payload.Sex.Trim());
        cmd.Parameters.AddWithValue("@email", payload.Email.Trim());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Aluno não encontrado." });
        }

        var inactiveAtOrdinal = reader.GetOrdinal("inactive_at");

        var aluno = new StudentDetail(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("full_name")),
            reader.GetDateTime(reader.GetOrdinal("birth_date")),
            reader.GetString(reader.GetOrdinal("sex")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(inactiveAtOrdinal) ? null : reader.GetDateTime(inactiveAtOrdinal)
        );

        return Results.Ok(new { mensagem = "Aluno atualizado com sucesso.", aluno });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro PUT /api/alunos/{{id}}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpdateAluno");

// Inativa aluno (soft delete) usando colunas de status quando disponíveis.
app.MapDelete("/api/alunos/{id:long}/inativar", async (long id) =>
{
    if (!hasUsersTable)
    {
        return Results.Problem(
            detail: "Tabela public.users não encontrada no banco.",
            title: "Regra de negócio indisponível",
            statusCode: 500);
    }

    var setClauses = new List<string>();
    if (hasUsersIsActive)
    {
        setClauses.Add("is_active = false");
    }
    if (hasUsersActive)
    {
        setClauses.Add("active = false");
    }
    if (hasUsersStatus)
    {
        setClauses.Add("status = 'I'");
    }
    if (hasUsersInactiveAt)
    {
        setClauses.Add("inactive_at = now()");
    }
    if (hasUsersUpdatedAt)
    {
        setClauses.Add("updated_at = now()");
    }

    var hasInactivationField = hasUsersIsActive || hasUsersActive || hasUsersStatus || hasUsersInactiveAt;
    if (!hasInactivationField)
    {
        return Results.BadRequest(new
        {
            mensagem = "Schema de users não possui colunas para inativação (is_active, active, status ou inactive_at)."
        });
    }

    try
    {
        var sql = $@"
            update public.users
            set {string.Join(", ", setClauses)}
            where id = @id
            returning id";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync();
        if (result is null)
        {
            return Results.NotFound(new { mensagem = "Aluno não encontrado." });
        }

        return Results.Ok(new { mensagem = "Aluno inativado com sucesso.", id });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/alunos/{{id}}/inativar: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("InactivateAluno");

// Reativa aluno (desfaz soft delete) usando colunas de status quando disponíveis.
app.MapPost("/api/alunos/{id:long}/reativar", async (long id) =>
{
    if (!hasUsersTable)
    {
        return Results.Problem(
            detail: "Tabela public.users não encontrada no banco.",
            title: "Regra de negócio indisponível",
            statusCode: 500);
    }

    var setClauses = new List<string>();
    if (hasUsersIsActive)
    {
        setClauses.Add("is_active = true");
    }
    if (hasUsersActive)
    {
        setClauses.Add("active = true");
    }
    if (hasUsersStatus)
    {
        setClauses.Add("status = 'A'");
    }
    if (hasUsersInactiveAt)
    {
        setClauses.Add("inactive_at = null");
    }
    if (hasUsersUpdatedAt)
    {
        setClauses.Add("updated_at = now()");
    }

    var hasInactivationField = hasUsersIsActive || hasUsersActive || hasUsersStatus || hasUsersInactiveAt;
    if (!hasInactivationField)
    {
        return Results.BadRequest(new
        {
            mensagem = "Schema de users não possui colunas para reativação (is_active, active, status ou inactive_at)."
        });
    }

    try
    {
        var sql = $@"
            update public.users
            set {string.Join(", ", setClauses)}
            where id = @id
            returning id";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@id", id);

        var result = await cmd.ExecuteScalarAsync();
        if (result is null)
        {
            return Results.NotFound(new { mensagem = "Aluno não encontrado." });
        }

        return Results.Ok(new { mensagem = "Aluno reativado com sucesso.", id });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/alunos/{{id}}/reativar: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ReactivateAluno");


// Inicia o servidor web e bloqueia o thread principal.
app.Run();

// Record imutável que guarda data, temperatura em Celsius e descrição; a propriedade calculada converte o valor para Fahrenheit usando a fórmula tradicional.
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
record PersonSubmission(string Name, string BirthDate, string Email);
record LoginRequest(string Email, string Password);
record RegisterRequest(string FullName, string BirthDate, string Sex, string Email, string Password, string ConfirmPassword);
record Instrument(int Id, string Name);
record InstrumentCreate(string Name);
record StudentListItem(long Id, string FullName, DateTime BirthDate, string Sex, string Email, bool IsActive);
record StudentDetail(long Id, string FullName, DateTime BirthDate, string Sex, string Email, bool IsActive, DateTime? InactiveAt);
record StudentUpdateRequest(string FullName, string BirthDate, string Sex, string Email);




