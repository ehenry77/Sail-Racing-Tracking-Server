import { WebSocket } from 'ws';

export type ClientRole = 'competitor' | 'spectator' | 'committee';

interface ClientInfo {
  socket: WebSocket;
  raceId: string;
  role: ClientRole;
  participantId?: string;
}

const clientsByRace = new Map<string, Set<ClientInfo>>();
const infoBySocket = new Map<WebSocket, ClientInfo>();

export function registerClient(socket: WebSocket, raceId: string, role: ClientRole, participantId?: string): void {
  const info: ClientInfo = { socket, raceId, role, participantId };
  infoBySocket.set(socket, info);

  if (!clientsByRace.has(raceId)) {
    clientsByRace.set(raceId, new Set());
  }
  clientsByRace.get(raceId)!.add(info);
}

export function unregisterClient(socket: WebSocket): void {
  const info = infoBySocket.get(socket);
  if (!info) {
    return;
  }

  clientsByRace.get(info.raceId)?.delete(info);
  infoBySocket.delete(socket);
}

export function getClientInfo(socket: WebSocket): ClientInfo | undefined {
  return infoBySocket.get(socket);
}

export function broadcast(raceId: string, message: unknown, exclude?: WebSocket): void {
  const json = JSON.stringify(message);
  const clients = clientsByRace.get(raceId);
  if (!clients) {
    return;
  }

  for (const client of clients) {
    if (client.socket !== exclude && client.socket.readyState === WebSocket.OPEN) {
      client.socket.send(json);
    }
  }
}
