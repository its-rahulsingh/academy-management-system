"""Quick offline checks that the deployment contract is intact. Not a substitute for the test suite."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


dockerfile = read("Dockerfile")
assert "ASPNETCORE_URLS=http://0.0.0.0:10000" in dockerfile
assert "/source/client/dist/academy-web/browser/" in dockerfile

render = read("render.yaml")
assert "ConnectionStrings__AcademyDb" in render
assert "healthCheckPath: /api/health" in render

startup = read("server/Academy.Api/Infrastructure/Startup.cs")
assert "AddEndpointFilter<TenantAccessFilter>()" in startup, "academy endpoints must sit behind the tenant gate"
assert "MigrateAsync()" in startup

context = read("server/Academy.Api/Data/AcademyDbContext.cs")
assert "HasQueryFilter(e => e.TenantId == CurrentTenantId)" in context, "tenant query filter missing"
assert "Cross-tenant write" in context, "tenant write guard missing"

print("Source checks OK.")
