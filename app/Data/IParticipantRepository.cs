using SailRacing.Models;

namespace SailRacing.Data;

public interface IParticipantRepository
{
    Task<List<Participant>> GetAllAsync();

    Task<Participant?> GetByIdAsync(string id);

    Task<List<Participant>> GetByIdsAsync(IEnumerable<string> ids);

    Task SaveAsync(Participant participant);

    Task DeleteAsync(Participant participant);
}
