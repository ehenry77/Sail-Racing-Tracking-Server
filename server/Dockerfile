FROM node:22-slim AS build
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm install
COPY tsconfig.json ./
COPY src ./src
RUN npm run build

FROM node:22-slim
WORKDIR /app
ENV NODE_ENV=production
COPY package.json package-lock.json* ./
RUN npm install --omit=dev
COPY --from=build /app/dist ./dist
COPY public ./public

# Persisted so the SQLite DB survives container restarts when this path is mounted as a volume.
ENV DB_PATH=/app/data/sailracing.db
VOLUME ["/app/data"]

EXPOSE 3000
CMD ["node", "dist/index.js"]
