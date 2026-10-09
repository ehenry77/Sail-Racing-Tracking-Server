import path from 'path';
import express from 'express';
import racesRouter from './routes/races';
import { startTrackRecorder } from './services/trackRecorder';
import { attachWebSocketServer } from './ws/handlers';

const app = express();
app.use(express.json());

app.use('/api/races', racesRouter);

// Competitor join page: /race/:joinCode -> static SPA that reads the code from the URL.
app.use('/competitor', express.static(path.join(__dirname, '..', 'public', 'competitor')));
app.get('/race/:code', (_req, res) => {
  res.sendFile(path.join(__dirname, '..', 'public', 'competitor', 'index.html'));
});

// Replay of a finished (or running) race from its recorded GPS traces.
app.use('/replay', express.static(path.join(__dirname, '..', 'public', 'replay')));

// Live map for spectators/committee.
app.use('/map', express.static(path.join(__dirname, '..', 'public', 'map')));
app.get('/', (_req, res) => res.redirect('/map'));

app.get('/healthz', (_req, res) => res.json({ ok: true }));

const port = Number(process.env.PORT ?? 3000);
const server = app.listen(port, () => {
  console.log(`Sail-Racing server listening on port ${port}`);
});

attachWebSocketServer(server);
startTrackRecorder();
