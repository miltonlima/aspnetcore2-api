using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
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
var isDevelopment = app.Environment.IsDevelopment();

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
var hasUsersImgPerfil = userColumns.Contains("img_perfil");
const long MaxProfileImageBytes = 1024 * 1024;
var allowedProfileImageTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "image/jpeg",
    "image/png",
    "image/webp",
    "image/gif"
};
var allowedProfileImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".jpg",
    ".jpeg",
    ".png",
    ".webp",
    ".gif"
};

var hasPerfilAcessoTable = false;
var hasUsuarioPerfilAcessoTable = false;
try
{
    await using var roleTablesCmd = dataSource.CreateCommand(@"
        select table_name
        from information_schema.tables
        where table_schema = 'public'
          and table_name in ('perfil_acesso', 'usuario_perfil_acesso')");
    await using var roleTablesReader = await roleTablesCmd.ExecuteReaderAsync();
    while (await roleTablesReader.ReadAsync())
    {
        var tableName = roleTablesReader.GetString(0);
        if (tableName.Equals("perfil_acesso", StringComparison.OrdinalIgnoreCase))
        {
            hasPerfilAcessoTable = true;
        }
        else if (tableName.Equals("usuario_perfil_acesso", StringComparison.OrdinalIgnoreCase))
        {
            hasUsuarioPerfilAcessoTable = true;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Falha ao ler schema de tabelas RBAC: {ex.Message}");
}

var hasRbacTables = hasPerfilAcessoTable && hasUsuarioPerfilAcessoTable;

bool TryGetActorUserId(HttpRequest request, out long userId)
{
    userId = 0;

    var fromHeader = request.Headers["x-user-id"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(fromHeader) && long.TryParse(fromHeader, out var parsedHeaderId) && parsedHeaderId > 0)
    {
        userId = parsedHeaderId;
        return true;
    }

    var fromQuery = request.Query["userId"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(fromQuery) && long.TryParse(fromQuery, out var parsedQueryId) && parsedQueryId > 0)
    {
        userId = parsedQueryId;
        return true;
    }

    return false;
}

async Task<List<string>> GetUserRoleCodesAsync(long userId)
{
    if (!hasRbacTables)
    {
        return new List<string>();
    }

    var roles = new List<string>();
    await using var cmd = dataSource.CreateCommand(@"
        select pa.codigo
        from public.usuario_perfil_acesso up
        inner join public.perfil_acesso pa on pa.id = up.perfil_id
        where up.user_id = @user_id
          and up.active = true
          and (up.data_fim is null or up.data_fim >= current_date)
          and pa.active = true
        order by up.principal desc, pa.nivel desc, pa.codigo");
    cmd.Parameters.AddWithValue("@user_id", userId);

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(reader.GetString(0));
    }

    return roles;
}

async Task<IResult?> AuthorizeByRoleAsync(HttpRequest request, params string[] allowedRoles)
{
    if (!hasRbacTables)
    {
        return Results.Problem(
            detail: "Tabelas RBAC não encontradas. Execute o script sql/04_create_user_profiles_rbac.sql no Supabase.",
            title: "Estrutura de permissões ausente",
            statusCode: 500);
    }

    if (!TryGetActorUserId(request, out var actorUserId))
    {
        return Results.BadRequest(new { mensagem = "Informe o usuário autenticado via header x-user-id ou query userId." });
    }

    var roleCodes = await GetUserRoleCodesAsync(actorUserId);
    var hasAllowedRole = roleCodes.Any(role => allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

    // Fallback de bootstrap apenas em desenvolvimento: se não existir nenhum usuário
    // privilegiado ainda, permite temporariamente usuários autenticados com qualquer perfil ativo.
    if (!hasAllowedRole && isDevelopment)
    {
        await using var hasPrivilegedCmd = dataSource.CreateCommand(@"
            select 1
            from public.usuario_perfil_acesso up
            inner join public.perfil_acesso pa on pa.id = up.perfil_id
            where up.active = true
              and (up.data_fim is null or up.data_fim >= current_date)
              and pa.active = true
              and pa.codigo in ('ADMINISTRADOR', 'GERENTE', 'COORDENADOR', 'PROFESSOR')
            limit 1");
        var privilegedExists = await hasPrivilegedCmd.ExecuteScalarAsync() is not null;

        if (!privilegedExists && roleCodes.Count > 0)
        {
            return null;
        }
    }

    if (!hasAllowedRole)
    {
        return Results.Json(new
        {
            mensagem = "Usuário não possui perfil permitido para esta rota.",
            userId = actorUserId,
            perfisUsuario = roleCodes,
            perfisPermitidos = allowedRoles,
            dica = "Vincule um perfil adequado usando os endpoints /api/admin/usuarios/{userId}/perfis."
        }, statusCode: 403);
    }

    return null;
}

// Aplica CORS antes de demais middlewares para garantir cabeçalhos em erros.
app.UseCors("AllowFrontend");
app.UseStaticFiles();
var uploadsStaticPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsStaticPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsStaticPath),
    RequestPath = "/uploads"
});

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
                coalesce(t.active, true) as active,
                t.inicio_inscricao,
                t.fim_inscricao,
                t.img_curso,
                t.descricao,
                t.classificacao,
                coalesce(t.preco, 0) as preco
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
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetDecimal(12)
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

    if (payload.InicioInscricao.HasValue && payload.FimInscricao.HasValue && payload.FimInscricao < payload.InicioInscricao)
    {
        return Results.BadRequest(new { mensagem = "Fim da inscricao nao pode ser menor que inicio da inscricao." });
    }

    if (payload.Preco.HasValue && payload.Preco.Value < 0)
    {
        return Results.BadRequest(new { mensagem = "Preco nao pode ser negativo." });
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
            insert into public.turma (
                nome_turma,
                modalidade_id,
                data_inicio,
                data_fim,
                active,
                inicio_inscricao,
                fim_inscricao,
                img_curso,
                descricao,
                classificacao,
                preco
            )
            values (
                @nome_turma,
                @modalidade_id,
                @data_inicio,
                @data_fim,
                @active,
                @inicio_inscricao,
                @fim_inscricao,
                @img_curso,
                @descricao,
                @classificacao,
                @preco
            )
            returning id, nome_turma, modalidade_id, data_inicio, data_fim, coalesce(active, true), inicio_inscricao, fim_inscricao, img_curso, descricao, classificacao, coalesce(preco, 0)");
        cmd.Parameters.AddWithValue("@nome_turma", payload.NomeTurma.Trim());
        cmd.Parameters.AddWithValue("@modalidade_id", payload.ModalidadeId);
        cmd.Parameters.AddWithValue("@data_inicio", payload.DataInicio.HasValue ? payload.DataInicio.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@data_fim", payload.DataFim.HasValue ? payload.DataFim.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);
        cmd.Parameters.AddWithValue("@inicio_inscricao", payload.InicioInscricao.HasValue ? payload.InicioInscricao.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@fim_inscricao", payload.FimInscricao.HasValue ? payload.FimInscricao.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@img_curso", string.IsNullOrWhiteSpace(payload.ImgCurso) ? DBNull.Value : payload.ImgCurso.Trim());
        cmd.Parameters.AddWithValue("@descricao", string.IsNullOrWhiteSpace(payload.Descricao) ? DBNull.Value : payload.Descricao.Trim());
        cmd.Parameters.AddWithValue("@classificacao", string.IsNullOrWhiteSpace(payload.Classificacao) ? DBNull.Value : payload.Classificacao.Trim());
        cmd.Parameters.AddWithValue("@preco", payload.Preco ?? 0m);

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
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetDecimal(11)
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

    if (payload.InicioInscricao.HasValue && payload.FimInscricao.HasValue && payload.FimInscricao < payload.InicioInscricao)
    {
        return Results.BadRequest(new { mensagem = "Fim da inscrição não pode ser menor que início da inscrição." });
    }

    if (payload.Preco.HasValue && payload.Preco.Value < 0)
    {
        return Results.BadRequest(new { mensagem = "Preço não pode ser negativo." });
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
                active = @active,
                inicio_inscricao = @inicio_inscricao,
                fim_inscricao = @fim_inscricao,
                img_curso = @img_curso,
                descricao = @descricao,
                classificacao = @classificacao,
                preco = @preco
            where id = @id
            returning id, nome_turma, modalidade_id, data_inicio, data_fim, coalesce(active, true), inicio_inscricao, fim_inscricao, img_curso, descricao, classificacao, coalesce(preco, 0)");
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@nome_turma", payload.NomeTurma.Trim());
        cmd.Parameters.AddWithValue("@modalidade_id", payload.ModalidadeId);
        cmd.Parameters.AddWithValue("@data_inicio", payload.DataInicio.HasValue ? payload.DataInicio.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@data_fim", payload.DataFim.HasValue ? payload.DataFim.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);
        cmd.Parameters.AddWithValue("@inicio_inscricao", payload.InicioInscricao.HasValue ? payload.InicioInscricao.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@fim_inscricao", payload.FimInscricao.HasValue ? payload.FimInscricao.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@img_curso", string.IsNullOrWhiteSpace(payload.ImgCurso) ? DBNull.Value : payload.ImgCurso.Trim());
        cmd.Parameters.AddWithValue("@descricao", string.IsNullOrWhiteSpace(payload.Descricao) ? DBNull.Value : payload.Descricao.Trim());
        cmd.Parameters.AddWithValue("@classificacao", string.IsNullOrWhiteSpace(payload.Classificacao) ? DBNull.Value : payload.Classificacao.Trim());
        cmd.Parameters.AddWithValue("@preco", payload.Preco ?? 0m);

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
            reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetDecimal(11)
        );
        return Results.Ok(updated);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedColumn)
    {
        return Results.Problem(
            detail: "Schema da tabela turma desatualizado (coluna ausente). Execute o script sql/03_fix_updated_at_columns_and_trigger.sql no banco.",
            title: "Estrutura de banco incompatível",
            statusCode: 500);
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

// CRUD do banco de questoes nas tabelas public.pergunta e public.alternativa.
app.MapGet("/api/perguntas", async () =>
{
    try
    {
        var perguntas = new Dictionary<long, PerguntaDto>();
        await using var cmd = dataSource.CreateCommand(@"
            select
                p.id,
                p.enunciado,
                p.dificuldade,
                p.status,
                p.created_at,
                p.updated_at,
                a.id as alternativa_id,
                a.texto,
                a.correta,
                a.ordem
            from public.pergunta p
            left join public.alternativa a on a.pergunta_id = p.id
            order by p.id desc, a.ordem asc, a.id asc");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var perguntaId = reader.GetInt64(0);
            if (!perguntas.TryGetValue(perguntaId, out var pergunta))
            {
                pergunta = new PerguntaDto(
                    perguntaId,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    new List<AlternativaDto>(),
                    reader.GetDateTime(4),
                    reader.GetDateTime(5));
                perguntas.Add(perguntaId, pergunta);
            }

            if (!reader.IsDBNull(6))
            {
                pergunta.Alternativas.Add(new AlternativaDto(
                    reader.GetInt64(6),
                    reader.GetString(7),
                    reader.GetBoolean(8),
                    reader.GetInt32(9)));
            }
        }

        return Results.Ok(perguntas.Values);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabelas public.pergunta/public.alternativa nao encontradas. Execute o script SQL do banco de questoes no Supabase.",
            title: "Estrutura de banco indisponivel",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/perguntas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListPerguntas");

app.MapPost("/api/perguntas", async (PerguntaUpsertRequest? payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.Enunciado))
    {
        return Results.BadRequest(new { mensagem = "Enunciado e obrigatorio." });
    }

    var alternativas = (payload.Alternativas ?? new List<AlternativaUpsertRequest>())
        .Select((item, index) => new AlternativaUpsertRequest(
            item.Id,
            item.Texto?.Trim() ?? string.Empty,
            item.Correta,
            item.Ordem > 0 ? item.Ordem : index + 1))
        .Where(item => !string.IsNullOrWhiteSpace(item.Texto))
        .ToList();

    if (alternativas.Count < 2)
    {
        return Results.BadRequest(new { mensagem = "Informe pelo menos duas alternativas." });
    }

    if (alternativas.Count(item => item.Correta) != 1)
    {
        return Results.BadRequest(new { mensagem = "Informe exatamente uma alternativa correta." });
    }

    await using var conn = await dataSource.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        await using var perguntaCmd = new NpgsqlCommand(@"
            insert into public.pergunta (enunciado, dificuldade, status)
            values (@enunciado, @dificuldade, @status)
            returning id, enunciado, dificuldade, status, created_at, updated_at", conn, tx);
        perguntaCmd.Parameters.AddWithValue("@enunciado", payload.Enunciado.Trim());
        perguntaCmd.Parameters.AddWithValue("@dificuldade", string.IsNullOrWhiteSpace(payload.Dificuldade) ? "Facil" : payload.Dificuldade.Trim());
        perguntaCmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(payload.Status) ? "Ativa" : payload.Status.Trim());

        long perguntaId;
        PerguntaDto created;
        await using (var reader = await perguntaCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                await tx.RollbackAsync();
                return Results.Problem("Falha ao inserir pergunta.");
            }

            perguntaId = reader.GetInt64(0);
            created = new PerguntaDto(
                perguntaId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                new List<AlternativaDto>(),
                reader.GetDateTime(4),
                reader.GetDateTime(5));
        }

        foreach (var alternativa in alternativas.OrderBy(item => item.Ordem))
        {
            await using var alternativaCmd = new NpgsqlCommand(@"
                insert into public.alternativa (pergunta_id, texto, correta, ordem)
                values (@pergunta_id, @texto, @correta, @ordem)
                returning id, texto, correta, ordem", conn, tx);
            alternativaCmd.Parameters.AddWithValue("@pergunta_id", perguntaId);
            alternativaCmd.Parameters.AddWithValue("@texto", alternativa.Texto.Trim());
            alternativaCmd.Parameters.AddWithValue("@correta", alternativa.Correta);
            alternativaCmd.Parameters.AddWithValue("@ordem", alternativa.Ordem);

            await using var alternativaReader = await alternativaCmd.ExecuteReaderAsync();
            if (await alternativaReader.ReadAsync())
            {
                created.Alternativas.Add(new AlternativaDto(
                    alternativaReader.GetInt64(0),
                    alternativaReader.GetString(1),
                    alternativaReader.GetBoolean(2),
                    alternativaReader.GetInt32(3)));
            }
        }

        await tx.CommitAsync();
        return Results.Created($"/api/perguntas/{created.Id}", created);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.WriteLine($"Erro POST /api/perguntas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreatePergunta");

app.MapPut("/api/perguntas/{id:long}", async (long id, PerguntaUpsertRequest? payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.Enunciado))
    {
        return Results.BadRequest(new { mensagem = "Enunciado e obrigatorio." });
    }

    var alternativas = (payload.Alternativas ?? new List<AlternativaUpsertRequest>())
        .Select((item, index) => new AlternativaUpsertRequest(
            item.Id,
            item.Texto?.Trim() ?? string.Empty,
            item.Correta,
            item.Ordem > 0 ? item.Ordem : index + 1))
        .Where(item => !string.IsNullOrWhiteSpace(item.Texto))
        .ToList();

    if (alternativas.Count < 2)
    {
        return Results.BadRequest(new { mensagem = "Informe pelo menos duas alternativas." });
    }

    if (alternativas.Count(item => item.Correta) != 1)
    {
        return Results.BadRequest(new { mensagem = "Informe exatamente uma alternativa correta." });
    }

    await using var conn = await dataSource.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        await using var perguntaCmd = new NpgsqlCommand(@"
            update public.pergunta
            set enunciado = @enunciado,
                dificuldade = @dificuldade,
                status = @status
            where id = @id
            returning id, enunciado, dificuldade, status, created_at, updated_at", conn, tx);
        perguntaCmd.Parameters.AddWithValue("@id", id);
        perguntaCmd.Parameters.AddWithValue("@enunciado", payload.Enunciado.Trim());
        perguntaCmd.Parameters.AddWithValue("@dificuldade", string.IsNullOrWhiteSpace(payload.Dificuldade) ? "Facil" : payload.Dificuldade.Trim());
        perguntaCmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(payload.Status) ? "Ativa" : payload.Status.Trim());

        PerguntaDto updated;
        await using (var reader = await perguntaCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                await tx.RollbackAsync();
                return Results.NotFound(new { mensagem = "Pergunta nao encontrada." });
            }

            updated = new PerguntaDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                new List<AlternativaDto>(),
                reader.GetDateTime(4),
                reader.GetDateTime(5));
        }

        await using var deleteCmd = new NpgsqlCommand("delete from public.alternativa where pergunta_id = @pergunta_id", conn, tx);
        deleteCmd.Parameters.AddWithValue("@pergunta_id", id);
        await deleteCmd.ExecuteNonQueryAsync();

        foreach (var alternativa in alternativas.OrderBy(item => item.Ordem))
        {
            await using var alternativaCmd = new NpgsqlCommand(@"
                insert into public.alternativa (pergunta_id, texto, correta, ordem)
                values (@pergunta_id, @texto, @correta, @ordem)
                returning id, texto, correta, ordem", conn, tx);
            alternativaCmd.Parameters.AddWithValue("@pergunta_id", id);
            alternativaCmd.Parameters.AddWithValue("@texto", alternativa.Texto.Trim());
            alternativaCmd.Parameters.AddWithValue("@correta", alternativa.Correta);
            alternativaCmd.Parameters.AddWithValue("@ordem", alternativa.Ordem);

            await using var alternativaReader = await alternativaCmd.ExecuteReaderAsync();
            if (await alternativaReader.ReadAsync())
            {
                updated.Alternativas.Add(new AlternativaDto(
                    alternativaReader.GetInt64(0),
                    alternativaReader.GetString(1),
                    alternativaReader.GetBoolean(2),
                    alternativaReader.GetInt32(3)));
            }
        }

        await tx.CommitAsync();
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.WriteLine($"Erro PUT /api/perguntas/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpdatePergunta");

app.MapDelete("/api/perguntas/{id:long}", async (long id) =>
{
    try
    {
        await using var cmd = dataSource.CreateCommand("delete from public.pergunta where id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound(new { mensagem = "Pergunta nao encontrada." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/perguntas/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("DeletePergunta");

app.MapPost("/api/perguntas/{perguntaId:long}/alternativas", async (long perguntaId, AlternativaUpsertRequest? payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.Texto))
    {
        return Results.BadRequest(new { mensagem = "Texto da alternativa e obrigatorio." });
    }

    await using var conn = await dataSource.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        if (payload.Correta)
        {
            await using var clearCmd = new NpgsqlCommand("update public.alternativa set correta = false where pergunta_id = @pergunta_id", conn, tx);
            clearCmd.Parameters.AddWithValue("@pergunta_id", perguntaId);
            await clearCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(@"
            insert into public.alternativa (pergunta_id, texto, correta, ordem)
            values (@pergunta_id, @texto, @correta, @ordem)
            returning id, texto, correta, ordem", conn, tx);
        cmd.Parameters.AddWithValue("@pergunta_id", perguntaId);
        cmd.Parameters.AddWithValue("@texto", payload.Texto.Trim());
        cmd.Parameters.AddWithValue("@correta", payload.Correta);
        cmd.Parameters.AddWithValue("@ordem", payload.Ordem > 0 ? payload.Ordem : 1);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await tx.RollbackAsync();
            return Results.Problem("Falha ao inserir alternativa.");
        }

        var created = new AlternativaDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetInt32(3));

        await tx.CommitAsync();
        return Results.Created($"/api/alternativas/{created.Id}", created);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
        await tx.RollbackAsync();
        return Results.NotFound(new { mensagem = "Pergunta nao encontrada." });
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.WriteLine($"Erro POST /api/perguntas/{perguntaId}/alternativas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateAlternativa");

app.MapPut("/api/alternativas/{id:long}", async (long id, AlternativaUpsertRequest? payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.Texto))
    {
        return Results.BadRequest(new { mensagem = "Texto da alternativa e obrigatorio." });
    }

    await using var conn = await dataSource.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        long? perguntaId = null;
        await using (var perguntaCmd = new NpgsqlCommand("select pergunta_id from public.alternativa where id = @id", conn, tx))
        {
            perguntaCmd.Parameters.AddWithValue("@id", id);
            var result = await perguntaCmd.ExecuteScalarAsync();
            if (result is null)
            {
                await tx.RollbackAsync();
                return Results.NotFound(new { mensagem = "Alternativa nao encontrada." });
            }
            perguntaId = Convert.ToInt64(result);
        }

        if (payload.Correta)
        {
            await using var clearCmd = new NpgsqlCommand("update public.alternativa set correta = false where pergunta_id = @pergunta_id and id <> @id", conn, tx);
            clearCmd.Parameters.AddWithValue("@pergunta_id", perguntaId.Value);
            clearCmd.Parameters.AddWithValue("@id", id);
            await clearCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(@"
            update public.alternativa
            set texto = @texto,
                correta = @correta,
                ordem = @ordem
            where id = @id
            returning id, texto, correta, ordem", conn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@texto", payload.Texto.Trim());
        cmd.Parameters.AddWithValue("@correta", payload.Correta);
        cmd.Parameters.AddWithValue("@ordem", payload.Ordem > 0 ? payload.Ordem : 1);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await tx.RollbackAsync();
            return Results.NotFound(new { mensagem = "Alternativa nao encontrada." });
        }

        var updated = new AlternativaDto(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.GetInt32(3));

        await tx.CommitAsync();
        return Results.Ok(updated);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.WriteLine($"Erro PUT /api/alternativas/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpdateAlternativa");

app.MapDelete("/api/alternativas/{id:long}", async (long id) =>
{
    try
    {
        await using var cmd = dataSource.CreateCommand("delete from public.alternativa where id = @id");
        cmd.Parameters.AddWithValue("@id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound(new { mensagem = "Alternativa nao encontrada." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/alternativas/{id}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("DeleteAlternativa");

// Respostas de avaliacao geradas a partir das perguntas cadastradas.
app.MapGet("/api/avaliacoes/respostas", async (long? alunoId) =>
{
    try
    {
        var respostas = new Dictionary<long, AvaliacaoRespostaDto>();
        await using var cmd = dataSource.CreateCommand(@"
            select
                ar.id,
                ar.aluno_id,
                ar.aluno_nome,
                ar.total_perguntas,
                ar.total_corretas,
                ar.percentual,
                ar.status,
                ar.created_at,
                ari.id as item_id,
                ari.pergunta_id,
                ari.alternativa_id,
                ari.correta
            from public.avaliacao_resposta ar
            left join public.avaliacao_resposta_item ari on ari.resposta_id = ar.id
            where (@aluno_id is null or ar.aluno_id = @aluno_id)
            order by ar.id desc, ari.id asc");
        cmd.Parameters.AddWithValue("@aluno_id", NpgsqlTypes.NpgsqlDbType.Bigint, alunoId.HasValue ? alunoId.Value : DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var respostaId = reader.GetInt64(0);
            if (!respostas.TryGetValue(respostaId, out var resposta))
            {
                resposta = new AvaliacaoRespostaDto(
                    respostaId,
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetDateTime(7),
                    new List<AvaliacaoRespostaItemDto>());
                respostas.Add(respostaId, resposta);
            }

            if (!reader.IsDBNull(8))
            {
                resposta.Itens.Add(new AvaliacaoRespostaItemDto(
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.GetInt64(10),
                    reader.GetBoolean(11)));
            }
        }

        return Results.Ok(respostas.Values);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabelas public.avaliacao_resposta/public.avaliacao_resposta_item nao encontradas. Execute o script sql/07_create_avaliacao_respostas.sql no Supabase.",
            title: "Estrutura de banco indisponivel",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/avaliacoes/respostas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListAvaliacaoRespostas");

app.MapPost("/api/avaliacoes/respostas", async (AvaliacaoRespostaCreateRequest? payload) =>
{
    if (payload is null || payload.Respostas is null || payload.Respostas.Count == 0)
    {
        return Results.BadRequest(new { mensagem = "Informe pelo menos uma resposta." });
    }

    var respostasRecebidas = payload.Respostas
        .Where(item => item.PerguntaId > 0 && item.AlternativaId > 0)
        .ToList();

    if (respostasRecebidas.Count == 0)
    {
        return Results.BadRequest(new { mensagem = "Informe perguntas e alternativas validas." });
    }

    if (respostasRecebidas.Select(item => item.PerguntaId).Distinct().Count() != respostasRecebidas.Count)
    {
        return Results.BadRequest(new { mensagem = "Cada pergunta deve ter apenas uma resposta." });
    }

    await using var conn = await dataSource.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        var itens = new List<AvaliacaoRespostaItemDto>();

        foreach (var resposta in respostasRecebidas)
        {
            await using var checkCmd = new NpgsqlCommand(@"
                select a.correta
                from public.pergunta p
                inner join public.alternativa a on a.pergunta_id = p.id
                where p.id = @pergunta_id
                  and a.id = @alternativa_id
                  and p.status = 'Ativa'
                limit 1", conn, tx);
            checkCmd.Parameters.AddWithValue("@pergunta_id", resposta.PerguntaId);
            checkCmd.Parameters.AddWithValue("@alternativa_id", resposta.AlternativaId);

            var corretaValue = await checkCmd.ExecuteScalarAsync();
            if (corretaValue is null)
            {
                await tx.RollbackAsync();
                return Results.BadRequest(new { mensagem = $"Alternativa invalida para a pergunta {resposta.PerguntaId}." });
            }

            itens.Add(new AvaliacaoRespostaItemDto(
                0,
                resposta.PerguntaId,
                resposta.AlternativaId,
                Convert.ToBoolean(corretaValue)));
        }

        var totalPerguntas = itens.Count;
        var totalCorretas = itens.Count(item => item.Correta);
        var percentual = totalPerguntas == 0 ? 0m : Math.Round(totalCorretas * 100m / totalPerguntas, 2);

        await using var respostaCmd = new NpgsqlCommand(@"
            insert into public.avaliacao_resposta (aluno_id, aluno_nome, total_perguntas, total_corretas, percentual, status)
            values (@aluno_id, @aluno_nome, @total_perguntas, @total_corretas, @percentual, 'Concluida')
            returning id, aluno_id, aluno_nome, total_perguntas, total_corretas, percentual, status, created_at", conn, tx);
        respostaCmd.Parameters.AddWithValue("@aluno_id", NpgsqlTypes.NpgsqlDbType.Bigint, payload.AlunoId.HasValue ? payload.AlunoId.Value : DBNull.Value);
        respostaCmd.Parameters.AddWithValue("@aluno_nome", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.AlunoNome) ? DBNull.Value : payload.AlunoNome.Trim());
        respostaCmd.Parameters.AddWithValue("@total_perguntas", totalPerguntas);
        respostaCmd.Parameters.AddWithValue("@total_corretas", totalCorretas);
        respostaCmd.Parameters.AddWithValue("@percentual", percentual);

        AvaliacaoRespostaDto created;
        await using (var reader = await respostaCmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
            {
                await tx.RollbackAsync();
                return Results.Problem("Falha ao registrar avaliacao.");
            }

            created = new AvaliacaoRespostaDto(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetDecimal(5),
                reader.GetString(6),
                reader.GetDateTime(7),
                new List<AvaliacaoRespostaItemDto>());
        }

        foreach (var item in itens)
        {
            await using var itemCmd = new NpgsqlCommand(@"
                insert into public.avaliacao_resposta_item (resposta_id, pergunta_id, alternativa_id, correta)
                values (@resposta_id, @pergunta_id, @alternativa_id, @correta)
                returning id, pergunta_id, alternativa_id, correta", conn, tx);
            itemCmd.Parameters.AddWithValue("@resposta_id", created.Id);
            itemCmd.Parameters.AddWithValue("@pergunta_id", item.PerguntaId);
            itemCmd.Parameters.AddWithValue("@alternativa_id", item.AlternativaId);
            itemCmd.Parameters.AddWithValue("@correta", item.Correta);

            await using var itemReader = await itemCmd.ExecuteReaderAsync();
            if (await itemReader.ReadAsync())
            {
                created.Itens.Add(new AvaliacaoRespostaItemDto(
                    itemReader.GetInt64(0),
                    itemReader.GetInt64(1),
                    itemReader.GetInt64(2),
                    itemReader.GetBoolean(3)));
            }
        }

        await tx.CommitAsync();
        return Results.Created($"/api/avaliacoes/respostas/{created.Id}", created);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        await tx.RollbackAsync();
        return Results.Problem(
            detail: "Tabelas de respostas nao encontradas. Execute o script sql/07_create_avaliacao_respostas.sql no Supabase.",
            title: "Estrutura de banco indisponivel",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.WriteLine($"Erro POST /api/avaliacoes/respostas: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateAvaliacaoResposta");

// Endpoints do professor para gerenciar conteúdo de turma (módulos e aulas).
app.MapGet("/api/professor/turmas", async (HttpRequest request) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

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
                coalesce(t.active, true) as active,
                t.inicio_inscricao,
                t.fim_inscricao,
                t.img_curso,
                t.descricao,
                t.classificacao,
                coalesce(t.preco, 0) as preco
            from public.turma t
            inner join public.modalidade m on m.id = t.modalidade_id
            order by t.nome_turma");
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
                reader.GetBoolean(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetDecimal(12)
            ));
        }
        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/professor/turmas: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorListTurmas");

app.MapGet("/api/professor/turmas/{turmaId:long}/modulos", async (HttpRequest request, long turmaId) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (turmaId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Turma inválida." });
    }

    try
    {
        var items = new List<object>();
        await using var cmd = dataSource.CreateCommand(@"
            select id, turma_id, titulo, descricao, ordem, coalesce(active, true) as active, created_at, updated_at
            from public.turma_modulo
            where turma_id = @turma_id
            order by ordem, id");
        cmd.Parameters.AddWithValue("@turma_id", turmaId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt64(reader.GetOrdinal("id")),
                turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
                titulo = reader.GetString(reader.GetOrdinal("titulo")),
                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
                ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
                active = reader.GetBoolean(reader.GetOrdinal("active")),
                createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            });
        }
        return Results.Ok(items);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_modulo não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/professor/turmas/{turmaId}/modulos: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorListModulos");

app.MapPost("/api/professor/turmas/{turmaId:long}/modulos", async (HttpRequest request, long turmaId, ModuloCreateRequest? payload) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (turmaId <= 0 || payload is null || string.IsNullOrWhiteSpace(payload.Titulo) || payload.Ordem <= 0)
    {
        return Results.BadRequest(new { mensagem = "Turma, título e ordem do módulo são obrigatórios." });
    }

    try
    {
        await using var turmaCmd = dataSource.CreateCommand("select 1 from public.turma where id = @id limit 1");
        turmaCmd.Parameters.AddWithValue("@id", turmaId);
        var turmaExiste = await turmaCmd.ExecuteScalarAsync();
        if (turmaExiste is null)
        {
            return Results.NotFound(new { mensagem = "Turma não encontrada." });
        }

        await using var cmd = dataSource.CreateCommand(@"
            insert into public.turma_modulo (turma_id, titulo, descricao, ordem, active)
            values (@turma_id, @titulo, @descricao, @ordem, @active)
            returning id, turma_id, titulo, descricao, ordem, coalesce(active, true) as active, created_at, updated_at");
        cmd.Parameters.AddWithValue("@turma_id", turmaId);
        cmd.Parameters.AddWithValue("@titulo", payload.Titulo.Trim());
        cmd.Parameters.AddWithValue("@descricao", string.IsNullOrWhiteSpace(payload.Descricao) ? (object)DBNull.Value : payload.Descricao.Trim());
        cmd.Parameters.AddWithValue("@ordem", payload.Ordem);
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao criar módulo.");
        }

        return Results.Created($"/api/professor/modulos/{reader.GetInt64(reader.GetOrdinal("id"))}", new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
            titulo = reader.GetString(reader.GetOrdinal("titulo")),
            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
            ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
            active = reader.GetBoolean(reader.GetOrdinal("active")),
            createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_modulo não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/professor/turmas/{turmaId}/modulos: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorCreateModulo");

app.MapPut("/api/professor/modulos/{moduloId:long}", async (HttpRequest request, long moduloId, ModuloCreateRequest? payload) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (moduloId <= 0 || payload is null || string.IsNullOrWhiteSpace(payload.Titulo) || payload.Ordem <= 0)
    {
        return Results.BadRequest(new { mensagem = "Título e ordem do módulo são obrigatórios." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            update public.turma_modulo
            set
                titulo = @titulo,
                descricao = @descricao,
                ordem = @ordem,
                active = @active,
                updated_at = now()
            where id = @id
            returning id, turma_id, titulo, descricao, ordem, coalesce(active, true) as active, created_at, updated_at");
        cmd.Parameters.AddWithValue("@id", moduloId);
        cmd.Parameters.AddWithValue("@titulo", payload.Titulo.Trim());
        cmd.Parameters.AddWithValue("@descricao", string.IsNullOrWhiteSpace(payload.Descricao) ? (object)DBNull.Value : payload.Descricao.Trim());
        cmd.Parameters.AddWithValue("@ordem", payload.Ordem);
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Módulo não encontrado." });
        }

        return Results.Ok(new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
            titulo = reader.GetString(reader.GetOrdinal("titulo")),
            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
            ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
            active = reader.GetBoolean(reader.GetOrdinal("active")),
            createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_modulo não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro PUT /api/professor/modulos/{moduloId}: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorUpdateModulo");

app.MapDelete("/api/professor/modulos/{moduloId:long}", async (HttpRequest request, long moduloId) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (moduloId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Módulo inválido." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand("delete from public.turma_modulo where id = @id");
        cmd.Parameters.AddWithValue("@id", moduloId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound(new { mensagem = "Módulo não encontrado." });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_modulo não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/professor/modulos/{moduloId}: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorDeleteModulo");

app.MapGet("/api/professor/turmas/{turmaId:long}/aulas", async (HttpRequest request, long turmaId) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (turmaId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Turma inválida." });
    }

    try
    {
        var items = new List<object>();
        await using var cmd = dataSource.CreateCommand(@"
            select
                a.id,
                a.turma_id,
                a.modulo_id,
                coalesce(m.titulo, 'Geral') as modulo_titulo,
                a.titulo,
                a.descricao,
                a.duracao_minutos,
                a.ordem,
                a.video_url,
                coalesce(a.active, true) as active,
                a.created_at,
                a.updated_at
            from public.turma_aula a
            left join public.turma_modulo m on m.id = a.modulo_id
            where a.turma_id = @turma_id
            order by a.ordem, a.id");
        cmd.Parameters.AddWithValue("@turma_id", turmaId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt64(reader.GetOrdinal("id")),
                turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
                moduloId = reader.IsDBNull(reader.GetOrdinal("modulo_id")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("modulo_id")),
                moduloTitulo = reader.GetString(reader.GetOrdinal("modulo_titulo")),
                titulo = reader.GetString(reader.GetOrdinal("titulo")),
                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
                duracaoMinutos = reader.GetInt32(reader.GetOrdinal("duracao_minutos")),
                ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
                videoUrl = reader.IsDBNull(reader.GetOrdinal("video_url")) ? string.Empty : reader.GetString(reader.GetOrdinal("video_url")),
                active = reader.GetBoolean(reader.GetOrdinal("active")),
                createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            });
        }

        return Results.Ok(items);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_aula não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/professor/turmas/{turmaId}/aulas: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorListAulas");

app.MapPost("/api/professor/turmas/{turmaId:long}/aulas", async (HttpRequest request, long turmaId, AulaCreateRequest? payload) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (turmaId <= 0 || payload is null || string.IsNullOrWhiteSpace(payload.Titulo) || payload.Ordem <= 0)
    {
        return Results.BadRequest(new { mensagem = "Turma, título e ordem da aula são obrigatórios." });
    }

    if (payload.DuracaoMinutos < 0)
    {
        return Results.BadRequest(new { mensagem = "Duração da aula não pode ser negativa." });
    }

    try
    {
        if (payload.ModuloId.HasValue)
        {
            await using var moduloCmd = dataSource.CreateCommand(@"
                select 1
                from public.turma_modulo
                where id = @id and turma_id = @turma_id
                limit 1");
            moduloCmd.Parameters.AddWithValue("@id", payload.ModuloId.Value);
            moduloCmd.Parameters.AddWithValue("@turma_id", turmaId);
            var moduloValido = await moduloCmd.ExecuteScalarAsync();
            if (moduloValido is null)
            {
                return Results.BadRequest(new { mensagem = "Módulo inválido para esta turma." });
            }
        }

        await using var cmd = dataSource.CreateCommand(@"
            insert into public.turma_aula (turma_id, modulo_id, titulo, descricao, duracao_minutos, ordem, video_url, active)
            values (@turma_id, @modulo_id, @titulo, @descricao, @duracao_minutos, @ordem, @video_url, @active)
            returning id, turma_id, modulo_id, titulo, descricao, duracao_minutos, ordem, video_url, coalesce(active, true) as active, created_at, updated_at");
        cmd.Parameters.AddWithValue("@turma_id", turmaId);
        cmd.Parameters.AddWithValue("@modulo_id", payload.ModuloId.HasValue ? payload.ModuloId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@titulo", payload.Titulo.Trim());
        cmd.Parameters.AddWithValue("@descricao", string.IsNullOrWhiteSpace(payload.Descricao) ? (object)DBNull.Value : payload.Descricao.Trim());
        cmd.Parameters.AddWithValue("@duracao_minutos", payload.DuracaoMinutos);
        cmd.Parameters.AddWithValue("@ordem", payload.Ordem);
        cmd.Parameters.AddWithValue("@video_url", string.IsNullOrWhiteSpace(payload.VideoUrl) ? (object)DBNull.Value : payload.VideoUrl.Trim());
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao criar aula.");
        }

        return Results.Created($"/api/professor/aulas/{reader.GetInt64(reader.GetOrdinal("id"))}", new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
            moduloId = reader.IsDBNull(reader.GetOrdinal("modulo_id")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("modulo_id")),
            titulo = reader.GetString(reader.GetOrdinal("titulo")),
            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
            duracaoMinutos = reader.GetInt32(reader.GetOrdinal("duracao_minutos")),
            ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
            videoUrl = reader.IsDBNull(reader.GetOrdinal("video_url")) ? string.Empty : reader.GetString(reader.GetOrdinal("video_url")),
            active = reader.GetBoolean(reader.GetOrdinal("active")),
            createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_aula não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/professor/turmas/{turmaId}/aulas: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorCreateAula");

app.MapPut("/api/professor/aulas/{aulaId:long}", async (HttpRequest request, long aulaId, AulaCreateRequest? payload) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (aulaId <= 0 || payload is null || string.IsNullOrWhiteSpace(payload.Titulo) || payload.Ordem <= 0)
    {
        return Results.BadRequest(new { mensagem = "Título e ordem da aula são obrigatórios." });
    }

    if (payload.DuracaoMinutos < 0)
    {
        return Results.BadRequest(new { mensagem = "Duração da aula não pode ser negativa." });
    }

    try
    {
        await using var turmaCmd = dataSource.CreateCommand("select turma_id from public.turma_aula where id = @id limit 1");
        turmaCmd.Parameters.AddWithValue("@id", aulaId);
        var turmaIdObj = await turmaCmd.ExecuteScalarAsync();
        if (turmaIdObj is null)
        {
            return Results.NotFound(new { mensagem = "Aula não encontrada." });
        }

        var turmaId = Convert.ToInt64(turmaIdObj);

        if (payload.ModuloId.HasValue)
        {
            await using var moduloCmd = dataSource.CreateCommand(@"
                select 1
                from public.turma_modulo
                where id = @id and turma_id = @turma_id
                limit 1");
            moduloCmd.Parameters.AddWithValue("@id", payload.ModuloId.Value);
            moduloCmd.Parameters.AddWithValue("@turma_id", turmaId);
            var moduloValido = await moduloCmd.ExecuteScalarAsync();
            if (moduloValido is null)
            {
                return Results.BadRequest(new { mensagem = "Módulo inválido para esta turma." });
            }
        }

        await using var cmd = dataSource.CreateCommand(@"
            update public.turma_aula
            set
                modulo_id = @modulo_id,
                titulo = @titulo,
                descricao = @descricao,
                duracao_minutos = @duracao_minutos,
                ordem = @ordem,
                video_url = @video_url,
                active = @active,
                updated_at = now()
            where id = @id
            returning id, turma_id, modulo_id, titulo, descricao, duracao_minutos, ordem, video_url, coalesce(active, true) as active, created_at, updated_at");
        cmd.Parameters.AddWithValue("@id", aulaId);
        cmd.Parameters.AddWithValue("@modulo_id", payload.ModuloId.HasValue ? payload.ModuloId.Value : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@titulo", payload.Titulo.Trim());
        cmd.Parameters.AddWithValue("@descricao", string.IsNullOrWhiteSpace(payload.Descricao) ? (object)DBNull.Value : payload.Descricao.Trim());
        cmd.Parameters.AddWithValue("@duracao_minutos", payload.DuracaoMinutos);
        cmd.Parameters.AddWithValue("@ordem", payload.Ordem);
        cmd.Parameters.AddWithValue("@video_url", string.IsNullOrWhiteSpace(payload.VideoUrl) ? (object)DBNull.Value : payload.VideoUrl.Trim());
        cmd.Parameters.AddWithValue("@active", payload.Active ?? true);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Aula não encontrada." });
        }

        return Results.Ok(new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
            moduloId = reader.IsDBNull(reader.GetOrdinal("modulo_id")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("modulo_id")),
            titulo = reader.GetString(reader.GetOrdinal("titulo")),
            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
            duracaoMinutos = reader.GetInt32(reader.GetOrdinal("duracao_minutos")),
            ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
            videoUrl = reader.IsDBNull(reader.GetOrdinal("video_url")) ? string.Empty : reader.GetString(reader.GetOrdinal("video_url")),
            active = reader.GetBoolean(reader.GetOrdinal("active")),
            createdAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
            updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_aula não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro PUT /api/professor/aulas/{aulaId}: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorUpdateAula");

app.MapDelete("/api/professor/aulas/{aulaId:long}", async (HttpRequest request, long aulaId) =>
{
    var authError = await AuthorizeByRoleAsync(request, "PROFESSOR", "COORDENADOR", "GERENTE", "ADMINISTRADOR");
    if (authError is not null)
    {
        return authError;
    }

    if (aulaId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Aula inválida." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand("delete from public.turma_aula where id = @id");
        cmd.Parameters.AddWithValue("@id", aulaId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Results.NoContent() : Results.NotFound(new { mensagem = "Aula não encontrada." });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.turma_aula não encontrada. Execute o script SQL do LMS escolar.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/professor/aulas/{aulaId}: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ProfessorDeleteAula");

// Endpoint para listar inscrições do aluno.
app.MapGet("/api/inscricoes/aluno/{alunoId:long}", async (long alunoId) =>
{
    if (alunoId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Aluno inválido." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            select
                i.id,
                i.aluno_id,
                i.turma_id,
                i.status,
                i.created_at,
                t.nome_turma,
                t.modalidade_id,
                m.course_name as modalidade_nome
            from public.inscricao i
            inner join public.turma t on t.id = i.turma_id
            inner join public.modalidade m on m.id = t.modalidade_id
            where i.aluno_id = @aluno_id
            order by i.created_at desc");
        cmd.Parameters.AddWithValue("@aluno_id", alunoId);

        var inscricoes = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            inscricoes.Add(new
            {
                id = reader.GetInt64(reader.GetOrdinal("id")),
                alunoId = reader.GetInt64(reader.GetOrdinal("aluno_id")),
                turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
                turmaNome = reader.GetString(reader.GetOrdinal("nome_turma")),
                modalidadeId = reader.GetInt64(reader.GetOrdinal("modalidade_id")),
                modalidadeNome = reader.GetString(reader.GetOrdinal("modalidade_nome")),
                status = reader.GetString(reader.GetOrdinal("status")),
                createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
            });
        }

        return Results.Ok(inscricoes);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela de inscrição/turma/modalidade não encontrada.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/inscricoes/aluno/{alunoId}: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListInscricoesByAluno");

// Dashboard do aluno com progresso por turma (experiência estilo LMS escolar).
app.MapGet("/api/alunos/{alunoId:long}/dashboard", async (long alunoId) =>
{
    if (alunoId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Aluno inválido." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            select
                i.turma_id,
                t.nome_turma,
                m.course_name as modalidade_nome,
                i.created_at as inscricao_em,
                count(a.id) as total_aulas,
                count(a.id) filter (where coalesce(p.concluida, false)) as aulas_concluidas,
                coalesce(sum(a.duracao_minutos), 0) as total_minutos,
                max(p.ultimo_acesso_em) as ultimo_acesso_em
            from public.inscricao i
            inner join public.turma t on t.id = i.turma_id
            inner join public.modalidade m on m.id = t.modalidade_id
            left join public.turma_aula a
                on a.turma_id = t.id
                and coalesce(a.active, true) = true
            left join public.aluno_aula_progresso p
                on p.aula_id = a.id
                and p.aluno_id = i.aluno_id
            where i.aluno_id = @aluno_id
            group by i.turma_id, t.nome_turma, m.course_name, i.created_at
            order by coalesce(max(p.ultimo_acesso_em), i.created_at) desc");
        cmd.Parameters.AddWithValue("@aluno_id", alunoId);

        var turmas = new List<object>();
        var turmasConcluidas = 0;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var totalAulas = reader.GetInt64(reader.GetOrdinal("total_aulas"));
            var aulasConcluidas = reader.GetInt64(reader.GetOrdinal("aulas_concluidas"));
            var percentual = totalAulas > 0
                ? Math.Round((double)aulasConcluidas * 100d / totalAulas, 1)
                : 0d;

            if (percentual >= 100d)
            {
                turmasConcluidas++;
            }

            var ultimoAcessoOrdinal = reader.GetOrdinal("ultimo_acesso_em");

            turmas.Add(new
            {
                turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
                turmaNome = reader.GetString(reader.GetOrdinal("nome_turma")),
                modalidadeNome = reader.GetString(reader.GetOrdinal("modalidade_nome")),
                inscricaoEm = reader.GetDateTime(reader.GetOrdinal("inscricao_em")),
                totalAulas,
                aulasConcluidas,
                percentualProgresso = percentual,
                totalMinutos = reader.GetInt64(reader.GetOrdinal("total_minutos")),
                ultimoAcessoEm = reader.IsDBNull(ultimoAcessoOrdinal) ? (DateTime?)null : reader.GetDateTime(ultimoAcessoOrdinal)
            });
        }

        var resumo = new
        {
            totalTurmas = turmas.Count,
            turmasConcluidas
        };

        return Results.Ok(new { resumo, turmas });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabelas de aula/progresso ainda não existem. Execute o script SQL de LMS escolar no Supabase.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/alunos/{alunoId}/dashboard: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("GetAlunoDashboard");

// Lista aulas da turma com progresso do aluno.
app.MapGet("/api/turmas/{turmaId:long}/aulas", async (long turmaId, long alunoId) =>
{
    if (turmaId <= 0 || alunoId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Turma e aluno são obrigatórios." });
    }

    try
    {
        await using var matriculaCmd = dataSource.CreateCommand(@"
            select 1
            from public.inscricao
            where aluno_id = @aluno_id and turma_id = @turma_id
            limit 1");
        matriculaCmd.Parameters.AddWithValue("@aluno_id", alunoId);
        matriculaCmd.Parameters.AddWithValue("@turma_id", turmaId);

        var matriculado = await matriculaCmd.ExecuteScalarAsync();
        if (matriculado is null)
        {
            return Results.Forbid();
        }

        await using var cmd = dataSource.CreateCommand(@"
            select
                a.id,
                a.turma_id,
                a.modulo_id,
                coalesce(md.titulo, 'Geral') as modulo_titulo,
                md.ordem as modulo_ordem,
                a.titulo,
                a.descricao,
                a.duracao_minutos,
                a.ordem,
                a.video_url,
                coalesce(p.percentual, 0) as percentual,
                coalesce(p.concluida, false) as concluida,
                p.ultimo_acesso_em
            from public.turma_aula a
            left join public.turma_modulo md on md.id = a.modulo_id
            left join public.aluno_aula_progresso p
                on p.aula_id = a.id
                and p.aluno_id = @aluno_id
            where a.turma_id = @turma_id
              and coalesce(a.active, true) = true
            order by coalesce(md.ordem, 2147483647) asc, a.ordem asc, a.id asc");
        cmd.Parameters.AddWithValue("@aluno_id", alunoId);
        cmd.Parameters.AddWithValue("@turma_id", turmaId);

        var aulas = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ultimoAcessoOrdinal = reader.GetOrdinal("ultimo_acesso_em");
            aulas.Add(new
            {
                id = reader.GetInt64(reader.GetOrdinal("id")),
                turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
                moduloId = reader.IsDBNull(reader.GetOrdinal("modulo_id")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("modulo_id")),
                moduloTitulo = reader.GetString(reader.GetOrdinal("modulo_titulo")),
                moduloOrdem = reader.IsDBNull(reader.GetOrdinal("modulo_ordem")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("modulo_ordem")),
                titulo = reader.GetString(reader.GetOrdinal("titulo")),
                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
                duracaoMinutos = reader.GetInt32(reader.GetOrdinal("duracao_minutos")),
                ordem = reader.GetInt32(reader.GetOrdinal("ordem")),
                videoUrl = reader.IsDBNull(reader.GetOrdinal("video_url")) ? string.Empty : reader.GetString(reader.GetOrdinal("video_url")),
                percentual = reader.GetDouble(reader.GetOrdinal("percentual")),
                concluida = reader.GetBoolean(reader.GetOrdinal("concluida")),
                ultimoAcessoEm = reader.IsDBNull(ultimoAcessoOrdinal) ? (DateTime?)null : reader.GetDateTime(ultimoAcessoOrdinal)
            });
        }

        return Results.Ok(aulas);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabelas de aula/progresso ainda não existem. Execute o script SQL de LMS escolar no Supabase.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/turmas/{turmaId}/aulas: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListTurmaAulas");

// Salva ou atualiza progresso de aula.
app.MapPost("/api/aulas/{aulaId:long}/progresso", async (long aulaId, AulaProgressUpsertRequest? payload) =>
{
    if (aulaId <= 0 || payload is null || payload.AlunoId <= 0 || payload.TurmaId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Aula, aluno e turma são obrigatórios." });
    }

    var percentual = Math.Clamp(payload.Percentual, 0, 100);
    var concluida = payload.Concluida || percentual >= 100;

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            insert into public.aluno_aula_progresso
                (aluno_id, turma_id, aula_id, percentual, concluida, ultimo_acesso_em)
            values
                (@aluno_id, @turma_id, @aula_id, @percentual, @concluida, now())
            on conflict (aluno_id, aula_id)
            do update set
                turma_id = excluded.turma_id,
                percentual = excluded.percentual,
                concluida = excluded.concluida,
                ultimo_acesso_em = now(),
                updated_at = now()
            returning id, aluno_id, turma_id, aula_id, percentual, concluida, ultimo_acesso_em, updated_at");
        cmd.Parameters.AddWithValue("@aluno_id", payload.AlunoId);
        cmd.Parameters.AddWithValue("@turma_id", payload.TurmaId);
        cmd.Parameters.AddWithValue("@aula_id", aulaId);
        cmd.Parameters.AddWithValue("@percentual", percentual);
        cmd.Parameters.AddWithValue("@concluida", concluida);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem(detail: "Falha ao salvar progresso da aula.", title: "Erro no banco", statusCode: 500);
        }

        return Results.Ok(new
        {
            id = reader.GetInt64(reader.GetOrdinal("id")),
            alunoId = reader.GetInt64(reader.GetOrdinal("aluno_id")),
            turmaId = reader.GetInt64(reader.GetOrdinal("turma_id")),
            aulaId = reader.GetInt64(reader.GetOrdinal("aula_id")),
            percentual = reader.GetDouble(reader.GetOrdinal("percentual")),
            concluida = reader.GetBoolean(reader.GetOrdinal("concluida")),
            ultimoAcessoEm = reader.GetDateTime(reader.GetOrdinal("ultimo_acesso_em")),
            updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabelas de aula/progresso ainda não existem. Execute o script SQL de LMS escolar no Supabase.",
            title: "Estrutura de banco ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/aulas/{aulaId}/progresso: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("UpsertAulaProgresso");

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

// Consulta logs de acesso do frontend em public.access_log.
app.MapGet("/api/access-logs", async (string? search, string? action, string? pagePath, int? limit) =>
{
    try
    {
        var take = Math.Clamp(limit.GetValueOrDefault(100), 1, 500);

        await using var cmd = dataSource.CreateCommand(@"
            select
                id,
                user_id,
                user_email,
                user_name,
                user_type,
                session_id,
                page_path,
                page_title,
                action,
                http_method,
                ip_address::text,
                user_agent,
                referrer,
                status_code,
                metadata::text,
                created_at
            from public.access_log
            where (
                @search is null
                or lower(
                    coalesce(user_email, '') || ' ' ||
                    coalesce(user_name, '') || ' ' ||
                    coalesce(user_type, '') || ' ' ||
                    coalesce(page_path, '') || ' ' ||
                    coalesce(page_title, '') || ' ' ||
                    coalesce(action, '') || ' ' ||
                    coalesce(ip_address::text, '')
                ) like '%' || lower(@search) || '%'
            )
            and (@action is null or action = @action)
            and (@page_path is null or page_path = @page_path)
            order by created_at desc, id desc
            limit @limit");

        cmd.Parameters.AddWithValue("@search", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(search) ? DBNull.Value : search.Trim());
        cmd.Parameters.AddWithValue("@action", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(action) ? DBNull.Value : action.Trim());
        cmd.Parameters.AddWithValue("@page_path", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(pagePath) ? DBNull.Value : pagePath.Trim());
        cmd.Parameters.AddWithValue("@limit", NpgsqlTypes.NpgsqlDbType.Integer, take);

        var logs = new List<AccessLogListItem>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            logs.Add(new AccessLogListItem(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetDateTime(15)
            ));
        }

        return Results.Ok(logs);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.access_log nao encontrada. Execute o script sql/09_create_access_logs.sql no Supabase.",
            title: "Estrutura de logs ausente",
            statusCode: 500);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/access-logs: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("ListAccessLogs");

// Registra logs de acesso do frontend em public.access_log.
app.MapPost("/api/access-logs", async (HttpRequest request, AccessLogCreateRequest? payload) =>
{
    if (payload is null || string.IsNullOrWhiteSpace(payload.PagePath) || string.IsNullOrWhiteSpace(payload.Action))
    {
        return Results.BadRequest(new { mensagem = "Pagina acessada e acao sao obrigatorias." });
    }

    try
    {
        var forwardedFor = request.Headers["x-forwarded-for"].FirstOrDefault();
        var ipAddress = !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()
            : request.HttpContext.Connection.RemoteIpAddress?.ToString();

        var headerUserAgent = request.Headers.UserAgent.ToString();
        var userAgent = string.IsNullOrWhiteSpace(payload.UserAgent)
            ? headerUserAgent
            : payload.UserAgent.Trim();
        var referrer = request.Headers.Referer.ToString();
        var metadata = JsonSerializer.Serialize(payload.Metadata ?? new Dictionary<string, object?>());

        await using var cmd = dataSource.CreateCommand(@"
            insert into public.access_log (
                user_id,
                user_email,
                user_name,
                user_type,
                session_id,
                page_path,
                page_title,
                action,
                http_method,
                ip_address,
                user_agent,
                referrer,
                status_code,
                metadata
            )
            values (
                @user_id,
                @user_email,
                @user_name,
                @user_type,
                @session_id,
                @page_path,
                @page_title,
                @action,
                @http_method,
                nullif(@ip_address, '')::inet,
                @user_agent,
                @referrer,
                @status_code,
                @metadata::jsonb
            )
            returning id, created_at");

        cmd.Parameters.AddWithValue("@user_id", NpgsqlTypes.NpgsqlDbType.Bigint, payload.UserId.HasValue ? payload.UserId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@user_email", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.UserEmail) ? DBNull.Value : payload.UserEmail.Trim());
        cmd.Parameters.AddWithValue("@user_name", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.UserName) ? DBNull.Value : payload.UserName.Trim());
        cmd.Parameters.AddWithValue("@user_type", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.UserType) ? DBNull.Value : payload.UserType.Trim());
        cmd.Parameters.AddWithValue("@session_id", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.SessionId) ? DBNull.Value : payload.SessionId.Trim());
        cmd.Parameters.AddWithValue("@page_path", payload.PagePath.Trim());
        cmd.Parameters.AddWithValue("@page_title", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.PageTitle) ? DBNull.Value : payload.PageTitle.Trim());
        cmd.Parameters.AddWithValue("@action", payload.Action.Trim());
        cmd.Parameters.AddWithValue("@http_method", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.HttpMethod) ? DBNull.Value : payload.HttpMethod.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@ip_address", ipAddress ?? string.Empty);
        cmd.Parameters.AddWithValue("@user_agent", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(userAgent) ? DBNull.Value : userAgent);
        cmd.Parameters.AddWithValue("@referrer", NpgsqlTypes.NpgsqlDbType.Text, string.IsNullOrWhiteSpace(payload.Referrer) ? (string.IsNullOrWhiteSpace(referrer) ? DBNull.Value : referrer) : payload.Referrer.Trim());
        cmd.Parameters.AddWithValue("@status_code", NpgsqlTypes.NpgsqlDbType.Integer, payload.StatusCode.HasValue ? payload.StatusCode.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@metadata", metadata);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.Problem("Falha ao registrar log de acesso.");
        }

        return Results.Created($"/api/access-logs/{reader.GetInt64(0)}", new
        {
            id = reader.GetInt64(0),
            createdAt = reader.GetDateTime(1)
        });
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
    {
        return Results.Problem(
            detail: "Tabela public.access_log nao encontrada. Execute o script sql/09_create_access_logs.sql no Supabase.",
            title: "Estrutura de logs ausente",
            statusCode: 500);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidTextRepresentation)
    {
        return Results.BadRequest(new { mensagem = "IP informado em formato invalido." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/access-logs: {ex.Message}\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("CreateAccessLog");

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
        long userId;
        string fullName;
        DateTime birthDate;
        string sex;
        string email;
        string? imgPerfil;

        var loginImgPerfilSql = hasUsersImgPerfil ? "img_perfil" : "null::text";
        await using var cmd = dataSource.CreateCommand($@"
            select id, full_name, birth_date, sex, email, password_hash, {loginImgPerfilSql} as img_perfil
            from public.users
            where email = @email
            limit 1");
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

        userId = reader.GetInt64(reader.GetOrdinal("id"));
        fullName = reader.GetString(reader.GetOrdinal("full_name"));
        birthDate = reader.GetDateTime(reader.GetOrdinal("birth_date"));
        sex = reader.GetString(reader.GetOrdinal("sex"));
        email = reader.GetString(reader.GetOrdinal("email"));
        var imgPerfilOrdinal = reader.GetOrdinal("img_perfil");
        imgPerfil = reader.IsDBNull(imgPerfilOrdinal) ? null : reader.GetString(imgPerfilOrdinal);
        await reader.DisposeAsync();

        var roleCodes = await GetUserRoleCodesAsync(userId);
        var perfilPrincipal = roleCodes.FirstOrDefault() ?? "ALUNO";

        var user = new
        {
            id = userId,
            full_name = fullName,
            birth_date = birthDate,
            sex,
            email,
            img_perfil = imgPerfil,
            perfil = perfilPrincipal,
            perfis = roleCodes.Count > 0 ? roleCodes.ToArray() : new[] { "ALUNO" }
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

        var userId = reader.GetInt64(reader.GetOrdinal("id"));
        var fullName = reader.GetString(reader.GetOrdinal("full_name"));
        var birthDate = reader.GetDateTime(reader.GetOrdinal("birth_date"));
        var sex = reader.GetString(reader.GetOrdinal("sex"));
        var email = reader.GetString(reader.GetOrdinal("email"));
        await reader.DisposeAsync();

        if (hasRbacTables)
        {
            await using var roleCmd = dataSource.CreateCommand(@"
                insert into public.usuario_perfil_acesso (user_id, perfil_id, principal, active)
                select @user_id, p.id, true, true
                from public.perfil_acesso p
                where p.codigo = 'ALUNO'
                on conflict (user_id, perfil_id) do nothing");
            roleCmd.Parameters.AddWithValue("@user_id", userId);
            await roleCmd.ExecuteNonQueryAsync();
        }

        var user = new
        {
            id = userId,
            full_name = fullName,
            birth_date = birthDate,
            sex,
            email
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

// Endpoints administrativos para gestão de perfis por usuário.
app.MapPost("/api/admin/bootstrap-admin", async (HttpRequest request, BootstrapAdminRequest? payload) =>
{
    if (!hasRbacTables)
    {
        return Results.Problem(
            detail: "Tabelas RBAC não encontradas. Execute o script sql/04_create_user_profiles_rbac.sql no Supabase.",
            title: "Estrutura de permissões ausente",
            statusCode: 500);
    }

    if (!TryGetActorUserId(request, out var actorUserId) && (payload is null || payload.UserId <= 0))
    {
        return Results.BadRequest(new { mensagem = "Informe x-user-id (ou userId na query) ou envie userId no corpo." });
    }

    var targetUserId = payload?.UserId > 0 ? payload.UserId : actorUserId;

    try
    {
        await using var checkPrivilegedCmd = dataSource.CreateCommand(@"
            select 1
            from public.usuario_perfil_acesso up
            inner join public.perfil_acesso pa on pa.id = up.perfil_id
            where up.active = true
              and (up.data_fim is null or up.data_fim >= current_date)
              and pa.active = true
              and pa.codigo in ('ADMINISTRADOR', 'GERENTE')
            limit 1");
        var hasPrivilegedUser = await checkPrivilegedCmd.ExecuteScalarAsync() is not null;
        if (hasPrivilegedUser)
        {
            return Results.Conflict(new
            {
                mensagem = "Bootstrap bloqueado: já existe usuário ADMINISTRADOR/GERENTE.",
                dica = "Use os endpoints administrativos padrão para gerir perfis."
            });
        }

        await using var checkUserCmd = dataSource.CreateCommand("select 1 from public.users where id = @id limit 1");
        checkUserCmd.Parameters.AddWithValue("@id", targetUserId);
        var userExists = await checkUserCmd.ExecuteScalarAsync() is not null;
        if (!userExists)
        {
            return Results.NotFound(new { mensagem = "Usuário alvo não encontrado." });
        }

        await using var txConn = await dataSource.OpenConnectionAsync();
        await using var beginCmd = new NpgsqlCommand("begin", txConn);
        await beginCmd.ExecuteNonQueryAsync();

        await using var clearPrincipalCmd = new NpgsqlCommand(@"
            update public.usuario_perfil_acesso
            set principal = false, updated_at = now()
            where user_id = @user_id", txConn);
        clearPrincipalCmd.Parameters.AddWithValue("@user_id", targetUserId);
        await clearPrincipalCmd.ExecuteNonQueryAsync();

        await using var assignCmd = new NpgsqlCommand(@"
            insert into public.usuario_perfil_acesso (user_id, perfil_id, principal, active, data_inicio, data_fim, observacao)
            select @user_id, p.id, true, true, current_date, null, @observacao
            from public.perfil_acesso p
            where p.codigo = 'ADMINISTRADOR'
            on conflict (user_id, perfil_id)
            do update set
                principal = true,
                active = true,
                data_fim = null,
                observacao = excluded.observacao,
                updated_at = now()", txConn);
        assignCmd.Parameters.AddWithValue("@user_id", targetUserId);
        assignCmd.Parameters.AddWithValue("@observacao", "Bootstrap inicial de administrador");
        await assignCmd.ExecuteNonQueryAsync();

        await using var commitCmd = new NpgsqlCommand("commit", txConn);
        await commitCmd.ExecuteNonQueryAsync();

        return Results.Ok(new
        {
            mensagem = "Bootstrap concluído. Perfil ADMINISTRADOR vinculado com sucesso.",
            userId = targetUserId,
            perfil = "ADMINISTRADOR"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/admin/bootstrap-admin: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("AdminBootstrap");

app.MapGet("/api/admin/perfis", async (HttpRequest request) =>
{
    var authError = await AuthorizeByRoleAsync(request, "ADMINISTRADOR", "GERENTE");
    if (authError is not null)
    {
        return authError;
    }

    try
    {
        var items = new List<object>();
        await using var cmd = dataSource.CreateCommand(@"
            select id, codigo, nome, descricao, nivel, active
            from public.perfil_acesso
            order by nivel desc, codigo");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt16(reader.GetOrdinal("id")),
                codigo = reader.GetString(reader.GetOrdinal("codigo")),
                nome = reader.GetString(reader.GetOrdinal("nome")),
                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
                nivel = reader.GetInt16(reader.GetOrdinal("nivel")),
                active = reader.GetBoolean(reader.GetOrdinal("active"))
            });
        }

        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/admin/perfis: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("AdminListPerfis");

app.MapGet("/api/admin/usuarios/{userId:long}/perfis", async (HttpRequest request, long userId) =>
{
    var authError = await AuthorizeByRoleAsync(request, "ADMINISTRADOR", "GERENTE");
    if (authError is not null)
    {
        return authError;
    }

    if (userId <= 0)
    {
        return Results.BadRequest(new { mensagem = "Usuário inválido." });
    }

    try
    {
        var items = new List<object>();
        await using var cmd = dataSource.CreateCommand(@"
            select
                up.id,
                up.user_id,
                pa.codigo,
                pa.nome,
                pa.nivel,
                up.principal,
                up.active,
                up.data_inicio,
                up.data_fim,
                up.observacao
            from public.usuario_perfil_acesso up
            inner join public.perfil_acesso pa on pa.id = up.perfil_id
            where up.user_id = @user_id
            order by up.principal desc, pa.nivel desc, pa.codigo");
        cmd.Parameters.AddWithValue("@user_id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                id = reader.GetInt64(reader.GetOrdinal("id")),
                userId = reader.GetInt64(reader.GetOrdinal("user_id")),
                perfilCodigo = reader.GetString(reader.GetOrdinal("codigo")),
                perfilNome = reader.GetString(reader.GetOrdinal("nome")),
                perfilNivel = reader.GetInt16(reader.GetOrdinal("nivel")),
                principal = reader.GetBoolean(reader.GetOrdinal("principal")),
                active = reader.GetBoolean(reader.GetOrdinal("active")),
                dataInicio = reader.GetDateTime(reader.GetOrdinal("data_inicio")),
                dataFim = reader.IsDBNull(reader.GetOrdinal("data_fim")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("data_fim")),
                observacao = reader.IsDBNull(reader.GetOrdinal("observacao")) ? string.Empty : reader.GetString(reader.GetOrdinal("observacao"))
            });
        }

        return Results.Ok(items);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro GET /api/admin/usuarios/{userId}/perfis: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("AdminListUserPerfis");

app.MapPost("/api/admin/usuarios/{userId:long}/perfis", async (HttpRequest request, long userId, UserRoleAssignRequest? payload) =>
{
    var authError = await AuthorizeByRoleAsync(request, "ADMINISTRADOR", "GERENTE");
    if (authError is not null)
    {
        return authError;
    }

    if (userId <= 0 || payload is null || string.IsNullOrWhiteSpace(payload.PerfilCodigo))
    {
        return Results.BadRequest(new { mensagem = "Usuário e perfil são obrigatórios." });
    }

    try
    {
        await using var tx = await dataSource.OpenConnectionAsync();
        await using var beginCmd = new NpgsqlCommand("begin", tx);
        await beginCmd.ExecuteNonQueryAsync();

        await using var checkUserCmd = new NpgsqlCommand("select 1 from public.users where id = @id limit 1", tx);
        checkUserCmd.Parameters.AddWithValue("@id", userId);
        var userExists = await checkUserCmd.ExecuteScalarAsync();
        if (userExists is null)
        {
            await using var rollbackMissingUser = new NpgsqlCommand("rollback", tx);
            await rollbackMissingUser.ExecuteNonQueryAsync();
            return Results.NotFound(new { mensagem = "Usuário não encontrado." });
        }

        var perfilCodigo = payload.PerfilCodigo.Trim().ToUpperInvariant();
        await using var checkPerfilCmd = new NpgsqlCommand(@"
            select id
            from public.perfil_acesso
            where codigo = @codigo and active = true
            limit 1", tx);
        checkPerfilCmd.Parameters.AddWithValue("@codigo", perfilCodigo);
        var perfilIdObj = await checkPerfilCmd.ExecuteScalarAsync();
        if (perfilIdObj is null)
        {
            await using var rollbackMissingRole = new NpgsqlCommand("rollback", tx);
            await rollbackMissingRole.ExecuteNonQueryAsync();
            return Results.BadRequest(new { mensagem = "Perfil inválido ou inativo." });
        }

        if (payload.Principal)
        {
            await using var clearPrincipalCmd = new NpgsqlCommand(@"
                update public.usuario_perfil_acesso
                set principal = false, updated_at = now()
                where user_id = @user_id", tx);
            clearPrincipalCmd.Parameters.AddWithValue("@user_id", userId);
            await clearPrincipalCmd.ExecuteNonQueryAsync();
        }

        await using var upsertCmd = new NpgsqlCommand(@"
            insert into public.usuario_perfil_acesso (user_id, perfil_id, principal, active, data_inicio, data_fim, observacao)
            values (@user_id, @perfil_id, @principal, true, current_date, null, @observacao)
            on conflict (user_id, perfil_id)
            do update set
                principal = excluded.principal,
                active = true,
                data_fim = null,
                observacao = excluded.observacao,
                updated_at = now()", tx);
        upsertCmd.Parameters.AddWithValue("@user_id", userId);
        upsertCmd.Parameters.AddWithValue("@perfil_id", Convert.ToInt16(perfilIdObj));
        upsertCmd.Parameters.AddWithValue("@principal", payload.Principal);
        upsertCmd.Parameters.AddWithValue("@observacao", string.IsNullOrWhiteSpace(payload.Observacao) ? (object)DBNull.Value : payload.Observacao.Trim());
        await upsertCmd.ExecuteNonQueryAsync();

        await using var commitCmd = new NpgsqlCommand("commit", tx);
        await commitCmd.ExecuteNonQueryAsync();

        return Results.Ok(new { mensagem = "Perfil vinculado ao usuário com sucesso.", userId, perfilCodigo, principal = payload.Principal });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro POST /api/admin/usuarios/{userId}/perfis: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("AdminAssignPerfilToUser");

app.MapDelete("/api/admin/usuarios/{userId:long}/perfis/{perfilCodigo}", async (HttpRequest request, long userId, string perfilCodigo) =>
{
    var authError = await AuthorizeByRoleAsync(request, "ADMINISTRADOR", "GERENTE");
    if (authError is not null)
    {
        return authError;
    }

    if (userId <= 0 || string.IsNullOrWhiteSpace(perfilCodigo))
    {
        return Results.BadRequest(new { mensagem = "Usuário e perfil são obrigatórios." });
    }

    try
    {
        await using var cmd = dataSource.CreateCommand(@"
            update public.usuario_perfil_acesso up
            set
                active = false,
                principal = false,
                data_fim = current_date,
                updated_at = now()
            from public.perfil_acesso pa
            where up.perfil_id = pa.id
              and up.user_id = @user_id
              and pa.codigo = @perfil_codigo
              and up.active = true");
        cmd.Parameters.AddWithValue("@user_id", userId);
        cmd.Parameters.AddWithValue("@perfil_codigo", perfilCodigo.Trim().ToUpperInvariant());

        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows == 0)
        {
            return Results.NotFound(new { mensagem = "Perfil ativo não encontrado para este usuário." });
        }

        return Results.Ok(new { mensagem = "Perfil removido do usuário com sucesso.", userId, perfilCodigo = perfilCodigo.Trim().ToUpperInvariant() });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro DELETE /api/admin/usuarios/{userId}/perfis/{perfilCodigo}: {ex.Message}\\n{ex.StackTrace}");
        return Results.Problem(detail: ex.Message, title: "Internal Server Error", statusCode: 500);
    }
}).WithName("AdminRemovePerfilFromUser");

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
                coalesce(nullif(full_name, ''), email, 'Aluno sem nome') as full_name,
                coalesce(birth_date, date '1900-01-01') as birth_date,
                coalesce(sex, '') as sex,
                coalesce(email, '') as email,
                {statusSql} as is_active
            from public.users
            {whereSql}
            order by coalesce(nullif(full_name, ''), email, 'Aluno sem nome')";

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
        var imgPerfilSql = hasUsersImgPerfil ? "img_perfil" : "null::text";

        var sql = $@"
            select
                id,
                coalesce(nullif(full_name, ''), email, 'Aluno sem nome') as full_name,
                coalesce(birth_date, date '1900-01-01') as birth_date,
                coalesce(sex, '') as sex,
                coalesce(email, '') as email,
                {imgPerfilSql} as img_perfil,
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
        var imgPerfilOrdinal = reader.GetOrdinal("img_perfil");

        var aluno = new StudentDetail(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("full_name")),
            reader.GetDateTime(reader.GetOrdinal("birth_date")),
            reader.GetString(reader.GetOrdinal("sex")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(inactiveAtOrdinal) ? null : reader.GetDateTime(inactiveAtOrdinal),
            reader.IsDBNull(imgPerfilOrdinal) ? null : reader.GetString(imgPerfilOrdinal)
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
app.MapPut("/api/alunos/{id:long}", async (HttpRequest request, long id) =>
{
    if (!hasUsersTable)
    {
        return Results.Problem(
            detail: "Tabela public.users não encontrada no banco.",
            title: "Regra de negócio indisponível",
            statusCode: 500);
    }

    StudentUpdateRequest? payload;
    IFormFile? profileImage = null;

    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync();
        payload = new StudentUpdateRequest(
            form["fullName"].FirstOrDefault() ?? string.Empty,
            form["birthDate"].FirstOrDefault() ?? string.Empty,
            form["sex"].FirstOrDefault() ?? string.Empty,
            form["email"].FirstOrDefault() ?? string.Empty,
            form["password"].FirstOrDefault());
        profileImage = form.Files["imgPerfil"] ?? form.Files["img_perfil"] ?? form.Files["profileImage"];
    }
    else
    {
        payload = await JsonSerializer.DeserializeAsync<StudentUpdateRequest>(
            request.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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

    var shouldUpdatePassword = !string.IsNullOrWhiteSpace(payload.Password);
    if (shouldUpdatePassword && payload.Password!.Trim().Length < 4)
    {
        return Results.BadRequest(new { mensagem = "A nova senha deve ter pelo menos 4 caracteres." });
    }

    if (profileImage is not null)
    {
        if (!hasUsersImgPerfil)
        {
            return Results.Problem(
                detail: "Coluna public.users.img_perfil não encontrada. Execute o script 13_alter_users_add_img_perfil.sql no Supabase.",
                title: "Campo de imagem de perfil ausente",
                statusCode: 500);
        }

        if (profileImage.Length <= 0)
        {
            return Results.BadRequest(new { mensagem = "Imagem de perfil inválida." });
        }

        if (profileImage.Length > MaxProfileImageBytes)
        {
            return Results.BadRequest(new { mensagem = "A imagem de perfil deve ter no máximo 1 MB." });
        }

        var extension = Path.GetExtension(profileImage.FileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !allowedProfileImageExtensions.Contains(extension) ||
            !allowedProfileImageTypes.Contains(profileImage.ContentType))
        {
            return Results.BadRequest(new { mensagem = "Envie uma imagem válida nos formatos JPG, PNG, WEBP ou GIF." });
        }
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

        string? imgPerfilPath = null;
        if (profileImage is not null)
        {
            var webRoot = app.Environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
            }

            var uploadDirectory = Path.Combine(webRoot, "uploads", "perfis");
            Directory.CreateDirectory(uploadDirectory);

            var extension = Path.GetExtension(profileImage.FileName).ToLowerInvariant();
            var fileName = $"usuario-{id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";
            var absolutePath = Path.Combine(uploadDirectory, fileName);

            await using var fileStream = File.Create(absolutePath);
            await profileImage.CopyToAsync(fileStream);
            imgPerfilPath = $"/uploads/perfis/{fileName}";
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
        var passwordSetSql = shouldUpdatePassword ? ", password_hash = @password_hash" : string.Empty;
        var imgPerfilSetSql = !string.IsNullOrWhiteSpace(imgPerfilPath) ? ", img_perfil = @img_perfil" : string.Empty;
        var imgPerfilSql = hasUsersImgPerfil ? "img_perfil" : "null::text";
        var updatedAtSetSql = hasUsersUpdatedAt ? ", updated_at = now()" : string.Empty;

        var sql = $@"
            update public.users
            set
                full_name = @full_name,
                birth_date = @birth_date,
                sex = @sex,
                email = @email
                {passwordSetSql}
                {imgPerfilSetSql}
                {updatedAtSetSql}
            where id = @id
            returning
                id,
                full_name,
                birth_date,
                sex,
                email,
                {imgPerfilSql} as img_perfil,
                {statusSql} as is_active,
                {inactiveAtSql} as inactive_at";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@full_name", payload.FullName.Trim());
        cmd.Parameters.AddWithValue("@birth_date", parsedBirthDate.Date);
        cmd.Parameters.AddWithValue("@sex", payload.Sex.Trim());
        cmd.Parameters.AddWithValue("@email", payload.Email.Trim());
        if (shouldUpdatePassword)
        {
            cmd.Parameters.AddWithValue("@password_hash", payload.Password!.Trim());
        }
        if (!string.IsNullOrWhiteSpace(imgPerfilPath))
        {
            cmd.Parameters.AddWithValue("@img_perfil", imgPerfilPath);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { mensagem = "Aluno não encontrado." });
        }

        var inactiveAtOrdinal = reader.GetOrdinal("inactive_at");
        var imgPerfilOrdinal = reader.GetOrdinal("img_perfil");

        var aluno = new StudentDetail(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("full_name")),
            reader.GetDateTime(reader.GetOrdinal("birth_date")),
            reader.GetString(reader.GetOrdinal("sex")),
            reader.GetString(reader.GetOrdinal("email")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(inactiveAtOrdinal) ? null : reader.GetDateTime(inactiveAtOrdinal),
            reader.IsDBNull(imgPerfilOrdinal) ? null : reader.GetString(imgPerfilOrdinal)
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
record Turma(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("nomeTurma")] string NomeTurma,
    [property: JsonPropertyName("modalidadeId")] long ModalidadeId,
    [property: JsonPropertyName("modalidadeNome")] string ModalidadeNome,
    [property: JsonPropertyName("dataInicio")] DateTime? DataInicio,
    [property: JsonPropertyName("dataFim")] DateTime? DataFim,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("inicioInscricao")] DateTime? InicioInscricao,
    [property: JsonPropertyName("fimInscricao")] DateTime? FimInscricao,
    [property: JsonPropertyName("imgCurso")] string? ImgCurso,
    [property: JsonPropertyName("descricao")] string? Descricao,
    [property: JsonPropertyName("classificacao")] string? Classificacao,
    [property: JsonPropertyName("preco")] decimal Preco);
record TurmaCreate(
    [property: JsonPropertyName("nomeTurma")] string NomeTurma,
    [property: JsonPropertyName("modalidadeId")] long ModalidadeId,
    [property: JsonPropertyName("dataInicio")] DateTime? DataInicio,
    [property: JsonPropertyName("dataFim")] DateTime? DataFim,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("inicioInscricao")] DateTime? InicioInscricao,
    [property: JsonPropertyName("fimInscricao")] DateTime? FimInscricao,
    [property: JsonPropertyName("imgCurso")] string? ImgCurso,
    [property: JsonPropertyName("descricao")] string? Descricao,
    [property: JsonPropertyName("classificacao")] string? Classificacao,
    [property: JsonPropertyName("preco")] decimal? Preco);
record ModuloCreateRequest(string Titulo, string? Descricao, int Ordem, bool? Active);
record AulaCreateRequest(long? ModuloId, string Titulo, string? Descricao, int DuracaoMinutos, int Ordem, string? VideoUrl, bool? Active);
record InscricaoCreate(long AlunoId, long TurmaId);
record AulaProgressUpsertRequest(long AlunoId, long TurmaId, double Percentual, bool Concluida);
record AlternativaDto(long Id, string Texto, bool Correta, int Ordem);
record PerguntaDto(long Id, string Enunciado, string Dificuldade, string Status, List<AlternativaDto> Alternativas, DateTime CreatedAt, DateTime UpdatedAt);
record AlternativaUpsertRequest(long? Id, string Texto, bool Correta, int Ordem);
record PerguntaUpsertRequest(string Enunciado, string Dificuldade, string Status, List<AlternativaUpsertRequest>? Alternativas);
record AvaliacaoRespostaItemDto(long Id, long PerguntaId, long AlternativaId, bool Correta);
record AvaliacaoRespostaDto(long Id, long? AlunoId, string? AlunoNome, int TotalPerguntas, int TotalCorretas, decimal Percentual, string Status, DateTime CreatedAt, List<AvaliacaoRespostaItemDto> Itens);
record AvaliacaoRespostaItemRequest(long PerguntaId, long AlternativaId);
record AvaliacaoRespostaCreateRequest(long? AlunoId, string? AlunoNome, List<AvaliacaoRespostaItemRequest>? Respostas);
record AccessLogCreateRequest(long? UserId, string? UserEmail, string? UserName, string? UserType, string? SessionId, string PagePath, string? PageTitle, string Action, string? HttpMethod, string? Referrer, string? UserAgent, int? StatusCode, Dictionary<string, object?>? Metadata);
record AccessLogListItem(long Id, long? UserId, string? UserEmail, string? UserName, string? UserType, string? SessionId, string PagePath, string? PageTitle, string Action, string? HttpMethod, string? IpAddress, string? UserAgent, string? Referrer, int? StatusCode, string? Metadata, DateTime CreatedAt);
record StudentListItem(long Id, string FullName, DateTime BirthDate, string Sex, string Email, bool IsActive);
record StudentDetail(long Id, string FullName, DateTime BirthDate, string Sex, string Email, bool IsActive, DateTime? InactiveAt, string? ImgPerfil);
record StudentUpdateRequest(string FullName, string BirthDate, string Sex, string Email, string? Password);
record BootstrapAdminRequest(long UserId);
record UserRoleAssignRequest(string PerfilCodigo, bool Principal, string? Observacao);








