import { WebSocket, WebSocketServer } from 'ws';
import { Server as HttpServer } from 'http';
import { processPosition } from '../services/raceTracker';
import { rememberPosition } from '../services/positionCache';
import { recordPosition } from '../services/trackRecorder';
import { broadcast, ClientRole, getClientInfo, registerClient, unregisterClient } from './registry';

interface JoinMessage {
  type: 'join';
  raceId: string;
  role: ClientRole;
  participantId?: string | null;
}

interface PositionMessage {
  type: 'position';
  raceId: string;
  participantId: string;
  lat: number;
  lon: number;
  accuracy?: number;
  timestamp?: string;
}

export function attachWebSocketServer(server: HttpServer): void {
  const wss = new WebSocketServer({ server, path: '/ws' });

  wss.on('connection', (socket: WebSocket) => {
    socket.on('message', (raw) => {
      let message: JoinMessage | PositionMessage;
      try {
        message = JSON.parse(raw.toString());
      } catch {
        socket.send(JSON.stringify({ type: 'error', message: 'Invalid JSON' }));
        return;
      }

      if (message.type === 'join') {
        registerClient(socket, message.raceId, message.role, message.participantId ?? undefined);
        return;
      }

      if (message.type === 'position') {
        const info = getClientInfo(socket);
        if (!info) {
          socket.send(JSON.stringify({ type: 'error', message: 'Send join before position' }));
          return;
        }

        const timestamp = message.timestamp ?? new Date().toISOString();

        broadcast(
          info.raceId,
          {
            type: 'positionUpdate',
            participantId: message.participantId,
            lat: message.lat,
            lon: message.lon,
            accuracy: message.accuracy ?? null,
            timestamp
          },
          socket
        );

        rememberPosition(info.raceId, {
          participantId: message.participantId,
          lat: message.lat,
          lon: message.lon,
          accuracy: message.accuracy ?? null,
          timestamp
        });

        recordPosition(
          info.raceId,
          message.participantId,
          message.lat,
          message.lon,
          message.accuracy ?? null,
          timestamp
        );

        const events = processPosition(info.raceId, message.participantId, { lat: message.lat, lon: message.lon }, timestamp);
        for (const event of events) {
          broadcast(info.raceId, { type: event.type, ...event.payload });
        }
      }
    });

    socket.on('close', () => unregisterClient(socket));
  });
}

export { broadcast };
