# ============================================================================
#  SchoolLms — multi-tenant (Control Plane + maktab subdomenlari) yagona obraz
#  3 bosqich: (1) SPA build (node) → (2) API publish (.NET) → (3) ozg'in runtime
# ============================================================================

# ---------- 1) Frontend (Vite) build ----------
FROM node:20-alpine AS client
WORKDIR /client
COPY schoollms.client/package*.json ./
RUN npm ci
COPY schoollms.client/ ./
# Build-time env: frontend real domeningizni tanishi va REAL API'ga (mock emas) ulanishi uchun.
ARG VITE_ROOT_DOMAIN=maktab.uz
ARG VITE_USE_MOCK=false
ENV VITE_ROOT_DOMAIN=$VITE_ROOT_DOMAIN VITE_USE_MOCK=$VITE_USE_MOCK
RUN npm run build        # natija: /client/dist

# ---------- 2) Backend (.NET) publish ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Avval faqat csproj'lar — qatlam keshini saqlash uchun. Klient (esproj) Docker'da qurilmaydi.
COPY SchoolLms.Domain/SchoolLms.Domain.csproj SchoolLms.Domain/
COPY SchoolLms.Application/SchoolLms.Application.csproj SchoolLms.Application/
COPY SchoolLms.Infrastructure/SchoolLms.Infrastructure.csproj SchoolLms.Infrastructure/
COPY SchoolLms.Server/SchoolLms.Server.csproj SchoolLms.Server/
RUN dotnet restore SchoolLms.Server/SchoolLms.Server.csproj -p:BuildSpa=false
COPY SchoolLms.Domain/ SchoolLms.Domain/
COPY SchoolLms.Application/ SchoolLms.Application/
COPY SchoolLms.Infrastructure/ SchoolLms.Infrastructure/
COPY SchoolLms.Server/ SchoolLms.Server/
RUN dotnet publish SchoolLms.Server/SchoolLms.Server.csproj -c Release -o /app/publish \
    -p:BuildSpa=false --no-restore

# ---------- 3) Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
# Maktab mintaqasi (UTC+5). tzdata — TimeZoneInfo "Asia/Tashkent"ni topishi uchun;
# TZ — log/uchinchi-tomon kutubxonalar ham mahalliy vaqtda bo'lishi uchun.
ENV TZ=Asia/Tashkent
RUN apt-get update && apt-get install -y --no-install-recommends tzdata \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
# Qurilgan SPA'ni server statik papkasiga qo'yamiz (API ham, SPA ham bitta originda).
COPY --from=client /client/dist ./wwwroot
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolLms.Server.dll"]
