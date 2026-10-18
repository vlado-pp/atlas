using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Atlas.BuildingBlocks.Persistence;

public static class SqlErrors
{
    private const int UniqueIndexViolation = 2601;
    private const int PrimaryKeyViolation = 2627;

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or PrimaryKeyViolation };
}
