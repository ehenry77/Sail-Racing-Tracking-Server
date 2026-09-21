using SailRacing.Models;

namespace SailRacing.Data;

public interface IResultPublicationRepository
{
    Task<List<ResultPublication>> GetPendingAsync();

    Task SaveAsync(ResultPublication publication);
}
