# Despliegue — grimoire.drheavymetal.com

`2026-07-11` · desplegado y verificado.

## Dónde
Servidor `drheavyserver` (192.168.1.3, LAN), Ubuntu 25.04, Docker 28, Traefik v3.2 en :80/:443.
Acceso SSH sin 1Password: `id_ed25519` instalado en `~drheavymetal/.ssh/authorized_keys` del server.
DNS `grimoire.drheavymetal.com` → 79.112.119.182 (IP pública del equipo) → Traefik.

## Cómo (aislado, aditivo — no toca ningún otro servicio)
- Stack propio en `~/apps/grimoire/` (proyecto compose `grimoire`): `grimoire-db` (pgvector/pgvector:pg17,
  volumen `grimoire-db-data`, red `grimoire` interna), `grimoire-api` y `grimoire-front` (en la red
  `traefik_default` externa; sin puertos de host expuestos — Traefik los alcanza por nombre de contenedor).
- Imágenes construidas local y transferidas por `docker save`/`load`: `go2chaindev/grimoire-{api,front,worker}:latest`.
  El front hornea `VITE_API_URL=https://grimoire.drheavymetal.com/api` en build (mismo origen → sin CORS).
- Datos: `pg_dump -Fc` de la base dev (207 622 artistas + 175 230 embeddings + xy + edges + credits…) →
  restaurado en `grimoire-db` (índices HNSW/GIN reconstruidos). Es el patrón D5: se despliega el Postgres destilado.
- Traefik: **solo** se añadió `~/apps/traefik/dynamic/grimoire.yml` (routers `grimoire-front` y
  `grimoire-api` con `PathPrefix(/api)`, entrypoint websecure, certResolver `le`). Hot-reload, sin tocar `traefik.yml`.
- Secreto: `Jwt__SigningKey` (64 chars, generado) en `~/apps/grimoire/.env` (NUNCA commiteado). La guarda D28
  exige 32+ bytes fuera de Development — verificada.

## Verificación
Web 200 + cert Let's Encrypt, `/api` sirve datos reales, http→https 301, la app aprende del gusto (D33).
Desde la LAN no se ve por NAT hairpin; desde internet, sí. Los 13 contenedores previos intactos.

## Exposición declarada (D28)
Refresh tokens no revocables durante 16 días. Aceptado para un grupo de amigos; revisar antes de abrir a más gente.
