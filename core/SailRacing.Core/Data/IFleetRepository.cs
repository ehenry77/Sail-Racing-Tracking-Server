using SailRacing.Models;

namespace SailRacing.Data;

public interface IFleetRepository
{
    Task<List<Fleet>> GetAllAsync();

    Task<Fleet?> GetByIdAsync(string id);

    Task<List<string>> GetParticipantIdsAsync(string fleetId);

    Task SaveAsync(Fleet fleet, IEnumerable<string> participantIds);

    Task DeleteAsync(Fleet fleet);
}
