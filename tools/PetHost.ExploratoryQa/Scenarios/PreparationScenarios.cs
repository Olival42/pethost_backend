using System.Net;

namespace PetHost.ExploratoryQa.Scenarios;

/// <summary>Confere que a API está no ar e abre as sessões dos dados de teste.</summary>
internal static class PreparationScenarios
{
    private const string Area = "Preparação";

    /// <returns><c>false</c> quando não dá para seguir (API fora do ar ou sem os dados de teste).</returns>
    public static async Task<bool> RunAsync(QaContext context)
    {
        var run = context.Run;
        var password = context.Options.TestDataPassword;
        context.AddSecret("senha", password);

        var ready = await context.Api.GetAsync("/health/ready");
        run.Record("P-01", Area, "API no ar: Postgres e Redis respondem (`GET /health/ready`)", "200",
            ready.Describe(), ready.Status == HttpStatusCode.OK);
        if (ready.Status != HttpStatusCode.OK)
            return false;

        var accounts = new (string Email, string Role, Action<Session> Keep)[]
        {
            (QaContext.OwnerAnaEmail, "owner", s => context.Ana = s),
            (QaContext.OwnerBrunoEmail, "owner", s => context.Bruno = s),
            (QaContext.HostCarlaEmail, "host", s => context.Carla = s),
            (QaContext.HostDiegoEmail, "host", s => context.Diego = s),
        };

        var opened = 0;
        foreach (var (email, role, keep) in accounts)
        {
            var login = await context.LoginAsync(email, password, role);
            if (login.Status != HttpStatusCode.OK)
            {
                run.Record("P-02", Area, $"Login dos dados de teste ({email})", "200", login.Describe(), passed: false);
                run.Note("Sem as sessões dos dados de teste o roteiro para aqui. Confira SEED_TEST_DATA_ENABLED=true, " +
                    "a senha em PETHOST_QA_TEST_DATA_PASSWORD e, se veio 429, espere 5 minutos (limite de login por IP).");
                return false;
            }

            keep(context.ReadSession(login, role));
            opened++;
        }

        run.Record("P-02", Area, "Login das 4 contas dos dados de teste (2 tutores, 2 anfitriões)", "200 em todas",
            $"{opened} sessões abertas", passed: true);

        context.AddSecret("CPF", QaContext.OwnerAnaCpf);
        context.AddSecret("CPF", "529.982.247-25");

        if (context.Options.AdminEmail is { Length: > 0 } adminEmail && context.Options.AdminPassword is { Length: > 0 } adminPassword)
        {
            context.AddSecret("senha", adminPassword);
            var admin = await context.LoginAsync(adminEmail, adminPassword, "admin");
            run.Record("P-03", Area, "Login do admin do seed", "200", admin.Describe(), admin.Status == HttpStatusCode.OK);

            if (admin.Status == HttpStatusCode.OK)
                context.Admin = context.ReadSession(admin, "admin");
        }
        else
        {
            run.Skip("P-03", Area, "Login do admin do seed", "PETHOST_QA_ADMIN_EMAIL/PASSWORD não definidos");
        }

        return true;
    }
}
