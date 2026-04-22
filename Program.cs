using System.Globalization;
using System.Net;
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

static bool IsPrivateNetworkHost(string host)
{
    if (string.IsNullOrWhiteSpace(host))
    {
        return false;
    }

    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!IPAddress.TryParse(host, out var ip))
    {
        return false;
    }

    if (IPAddress.IsLoopback(ip))
    {
        return true;
    }

    var bytes = ip.GetAddressBytes();

    // Faixas privadas IPv4: 10.0.0.0/8, 172.16.0.0/12 e 192.168.0.0/16.
    return bytes.Length == 4 &&
           (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168));
}

// Add services to the container.
// Define política de CORS para permitir que o frontend Vite consuma a API localmente.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                {
                    return false;
                }

                if (origin.Equals("https://reactvite2-app.pages.dev", StringComparison.OrdinalIgnoreCase) ||
                    origin.Equals("https://aspnetcore2-api.onrender.com", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                return uri.Scheme is "http" or "https" && IsPrivateNetworkHost(uri.Host);
            })
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

// CRUD de modalidades na tabela public.modalidade.
app.MapGet("/api/modalidades", async () =>
{
    try
    {
        var items = new List<Modalidade>();
        await using var cmd = dataSource.CreateCommand("select id, course_name, created_at from public.modalidade order by id");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new Modalidade(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTime(2)
            ));
        }
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/modalidades: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListModalidades");

app.MapPost("/api/modalidades", async (ModalidadeCreate payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.CourseName))
    {
        return Results.BadRequest(new { mensagem = "Nome da modalidade é obrigatório." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            insert into public.modalidade (course_name)
            values (@course_name)
            returning id, course_name, created_at");
        cmd.Parameters.AddWithValue("@course_name", payload.CourseName.Trim());
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao inserir modalidade.");
        }

        var created = new Modalidade(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2)
        );
        return Results.Created($"/api/modalidades/{created.Id}", created);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/modalidades: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateModalidade");

app.MapPut("/api/modalidades/{id:long}", async (long id, ModalidadeCreate payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.CourseName))
    {
        return Results.BadRequest(new { mensagem = "Nome da modalidade é obrigatório." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            update public.modalidade
            set course_name = @course_name
            where id = @id
            returning id, course_name, created_at");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@course_name", payload.CourseName.Trim());
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Modalidade não encontrada." });
        }

        var updated = new Modalidade(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2)
        );
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro PUT /api/modalidades/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpdateModalidade");

app.MapDelete("/api/modalidades/{id:long}", async (long id) =>
{
    try
    {
        await using var cmd = dataSource.CreateCommand("delete from public.modalidade where id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound(new { mensagem = "Modalidade não encontrada." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/modalidades/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("DeleteModalidade");

// CRUD de turmas na tabela public.turma, relacionando com public.modalidade.
app.MapGet("/api/turmas", async () =>
{
    try
    {
        var items = new List<Turma>();
        await using var cmd = dataSource.CreateCommand(@"
            select
                t.id,
                t.nome_turma,
                t.modalidade_id,
                m.course_name,
                t.data_inicio,
                t.data_fim,
                coalesce(t.active, true) as active
            from public.turma t
            inner join public.modalidade m on m.id = t.modalidade_id
            order by t.id");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new Turma(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetBoolean(6)
            ));
        }
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/turmas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListTurmas");

app.MapPost("/api/turmas", async (TurmaCreate payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.NomeTurma) || payload.ModalidadeId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Nome da turma e modalidade são obrigatórios." });
    }

    if (payload.DataInicio.HasValue && payload.DataFim.HasValue && payload.DataFim < payload.DataInicio)
    {
        return Results.BadRequest(new { mensagem = "Data fim não pode ser menor que data início." });
    }

    try
    {
        await using var modalidadeCmd = dataSource.CreateCommand("select course_name from public.modalidade where id = @id");
        modalidadeCmd.Parameters.AddWithValue("@id", payload.ModalidadeId);
        var modalidadeNomeObj = await modalidadeCmd.ExecuteScalarAsync();
        if (modalidadeNomeObj is null)
        {
            return Results.BadRequest(new { mensagem = "Modalidade informada não existe." });
        }

        await using var cmd = dataSource.CreateCommand(@"
            insert into public.turma (nome_turma, modalidade_id, data_inicio, data_fim, active)
            values (@nome_turma, @modalidade_id, @data_inicio, @data_fim, @active)
            returning id, nome_turma, modalidade_id, data_inicio, data_fim, coalesce(active, true)");
        cmd.Parameters.AddWithValue("@nome_turma", payload.NomeTurma.Trim());
        cmd.Parameters.AddWithValue("@modalidade_id", payload.ModalidadeId);
        cmd.Parameters.AddWithValue("@data_inicio", payload.DataInicio.HasValue ? payload.DataInicio.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@data_fim", payload.DataFim.HasValue ? payload.DataFim.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao inserir turma.");
        }

        var created = new Turma(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            modalidadeNomeObj.ToString() ?? string.Empty,
            reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.GetBoolean(5)
        );
        return Results.Created($"/api/turmas/{created.Id}", created);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/turmas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateTurma");

app.MapPut("/api/turmas/{id:long}", async (long id, TurmaCreate payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.NomeTurma) || payload.ModalidadeId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Nome da turma e modalidade são obrigatórios." });
    }

    if (payload.DataInicio.HasValue && payload.DataFim.HasValue && payload.DataFim < payload.DataInicio)
    {
        return Results.BadRequest(new { mensagem = "Data fim não pode ser menor que data início." });
    }

    try
    {
        await using var modalidadeCmd = dataSource.CreateCommand("select course_name from public.modalidade where id = @id");
        modalidadeCmd.Parameters.AddWithValue("@id", payload.ModalidadeId);
        var modalidadeNomeObj = await modalidadeCmd.ExecuteScalarAsync();
        if (modalidadeNomeObj is null)
        {
            return Results.BadRequest(new { mensagem = "Modalidade informada não existe." });
        }

        await using var cmd = dataSource.CreateCommand(@"
            update public.turma
            set
                nome_turma = @nome_turma,
                modalidade_id = @modalidade_id,
                data_inicio = @data_inicio,
                data_fim = @data_fim,
                active = @active
            where id = @id
            returning id, nome_turma, modalidade_id, data_inicio, data_fim, coalesce(active, true)");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@nome_turma", payload.NomeTurma.Trim());
        cmd.Parameters.AddWithValue("@modalidade_id", payload.ModalidadeId);
        cmd.Parameters.AddWithValue("@data_inicio", payload.DataInicio.HasValue ? payload.DataInicio.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@data_fim", payload.DataFim.HasValue ? payload.DataFim.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Turma não encontrada." });
        }

        var updated = new Turma(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            modalidadeNomeObj.ToString() ?? string.Empty,
            reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.GetBoolean(5)
        );
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro PUT /api/turmas/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpdateTurma");

app.MapDelete("/api/turmas/{id:long}", async (long id) =>
{
    try
    {
        await using var cmd = dataSource.CreateCommand("delete from public.turma where id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound(new { mensagem = "Turma não encontrada." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/turmas/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("DeleteTurma");

// Endpoint de inscrição do aluno em uma turma ativa.
app.MapPost("/api/inscricoes", async (InscricaoCreate? payload) =>
{
    if (payload is null || payload.AlunoId <= 0 || payload.TurmaId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Aluno e turma são obrigatórios." });
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

        await using var alunoCmd = dataSource.CreateCommand($@"
            select id, full_name, {statusSql} as is_active
            from public.users
            where id = @aluno_id
            limit 1");
        alunoCmd.Parameters.AddWithValue("@aluno_id", payload.AlunoId);
        await using var alunoReader = await alunoCmd.ExecuteReaderAsync();

        if (!await alunoReader.ReadAsync())
        {
            return Results.BadRequest(new { mensagem = "Aluno não encontrado." });
        }

        var alunoNome = alunoReader.GetString(alunoReader.GetOrdinal("full_name"));
        var alunoAtivo = alunoReader.GetBoolean(alunoReader.GetOrdinal("is_active"));

        if (!alunoAtivo)
        {
            return Results.BadRequest(new { mensagem = "Aluno inativo não pode realizar inscrição." });
        }

        await using var turmaCmd = dataSource.CreateCommand(@"
            select
                t.id,
                t.nome_turma,
                t.modalidade_id,
                m.course_name,
                coalesce(t.active, true) as active
            from public.turma t
            inner join public.modalidade m on m.id = t.modalidade_id
            where t.id = @turma_id
            limit 1");
        turmaCmd.Parameters.AddWithValue("@turma_id", payload.TurmaId);
        await using var turmaReader = await turmaCmd.ExecuteReaderAsync();

        if (!await turmaReader.ReadAsync())
        {
            return Results.BadRequest(new { mensagem = "Turma não encontrada." });
        }

        var turmaNome = turmaReader.GetString(turmaReader.GetOrdinal("nome_turma"));
        var modalidadeId = turmaReader.GetInt64(turmaReader.GetOrdinal("modalidade_id"));
        var modalidadeNome = turmaReader.GetString(turmaReader.GetOrdinal("course_name"));
        var turmaAtiva = turmaReader.GetBoolean(turmaReader.GetOrdinal("active"));

        if (!turmaAtiva)
        {
            return Results.BadRequest(new { mensagem = "Esta turma está inativa e não aceita novas inscrições." });
        }

        await using var insertCmd = dataSource.CreateCommand(@"
            insert into public.inscricao (aluno_id, turma_id, status)
            values (@aluno_id, @turma_id, @status)
            returning id, aluno_id, turma_id, status, created_at");
        insertCmd.Parameters.AddWithValue("@aluno_id", payload.AlunoId);
        insertCmd.Parameters.AddWithValue("@turma_id", payload.TurmaId);
        insertCmd.Parameters.AddWithValue("@status", "ATIVA");

        await using var insertReader = await insertCmd.ExecuteReaderAsync();
        if (!await insertReader.ReadAsync())
        {
            return Results.Problem(detail: "Falha ao registrar inscrição.", title: "Erro no banco", statusCode: 500);
        }

        var inscricao = new
        {
            id = insertReader.GetInt64(insertReader.GetOrdinal("id")),
            alunoId = insertReader.GetInt64(insertReader.GetOrdinal("aluno_id")),
            alunoNome,
            turmaId = insertReader.GetInt64(insertReader.GetOrdinal("turma_id")),
            turmaNome,
            modalidadeId,
            modalidadeNome,
            status = insertReader.GetString(insertReader.GetOrdinal("status")),
            createdAt = insertReader.GetDateTime(insertReader.GetOrdinal("created_at"))
        };

        return Results.Created($"/api/inscricoes/{inscricao.id}", new
        {
            mensagem = "Inscrição realizada com sucesso.",
            inscricao
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new { mensagem = "O aluno já está inscrito nesta turma." });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.inscricao não encontrada. Execute o script SQL de criação no Supabase.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/inscricoes: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateInscricao");

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
record Modalidade(long Id, string CourseName, DateTime CreatedAt);
record ModalidadeCreate(string CourseName);
record Turma(long Id, string NomeTurma, long ModalidadeId, string ModalidadeNome, DateTime? DataInicio, DateTime? DataFim, bool Active);
record TurmaCreate(string NomeTurma, long ModalidadeId, DateTime? DataInicio, DateTime? DataFim, bool? Active);
record InscricaoCreate(long AlunoId, long TurmaId);
record StudentListItem(long Id, string FullName, DateTime BirthDate, string Sex, string Email, bool IsActive);
record StudentDetail(long Id, string FullName, DateTime BirthDate, string Sex, string Email, bool IsActive, DateTime? InactiveAt);
record StudentUpdateRequest(string FullName, string BirthDate, string Sex, string Email);




