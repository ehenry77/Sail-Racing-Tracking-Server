// Mirrors /shared/contracts/*.schema.json — kept in sync by hand with the C# models in app/Models.

export type RaceStatus = 'Setup' | 'StartSequence' | 'Racing' | 'Finished';

export type RaceParticipantStatus = 'Racing' | 'Finished' | 'DNF' | 'DNS' | 'RET' | 'OCS';

export interface ParticipantDto {
  id: string;
  name: string;
  helm: string;
  tcf: number;
}

export interface FleetDto {
  id: string;
  name: string;
  participantIds: string[];
  participants: ParticipantDto[];
}

export interface BuoyDto {
  id: string;
  sequence: number;
  name: string;
  latitude: number;
  longitude: number;
  capturedViaGps: boolean;
}

export interface StartLineDto {
  committeeLatitude: number;
  committeeLongitude: number;
  pinLatitude: number;
  pinLongitude: number;
}

export interface RaceParticipantDto {
  participantId: string;
  laps: number;
  lapsCompleted: number;
  isOnFinalLap: boolean;
  status: RaceParticipantStatus;
  finishTime: string | null;
  elapsedSeconds: number | null;
  correctedSeconds: number | null;
  rank: number | null;
}

export interface RaceDto {
  id: string;
  contractVersion: number;
  joinCode: string | null;
  name: string;
  status: RaceStatus;
  fleet: FleetDto;
  startLine: StartLineDto;
  finishSameAsStart: boolean;
  finishLatitude: number | null;
  finishLongitude: number | null;
  buoys: BuoyDto[];
  lapsDefault: number;
  raceParticipants: RaceParticipantDto[];
  startAt: string | null;
  shortenCourseAppliedAt: string | null;
}

export interface ResultEntryDto {
  participantId: string;
  status: RaceParticipantStatus;
  elapsedSeconds: number | null;
  correctedSeconds: number | null;
  rank: number | null;
}

export interface ResultPublicationDto {
  raceId: string;
  publishedAt: string;
  results: ResultEntryDto[];
}
