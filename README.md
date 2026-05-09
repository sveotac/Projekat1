# GitHub Commits

Multithread HTTP veb server koji pribavlja i prikazuje broj komitova po kontributoru za bilo koji javni GitHub repozitorijum.

## Šta radi

Unesite GitHub `owner/repo` i server vraća rangiranu listu kontributora sa ukupnim brojem komitova. Rezultati se keširaju u memoriji, tako da se ponovljeni upiti zovu iz keša.

## Arhitektura

```
HTTP listener (glavna nit)
        │
        ▼
  RequestQueue
        │
        ▼
Worker threads 
        │
        ▼
   Cache 
        │  miss
        ▼
  GitHub API
```

- **Glavna nit** — prima dolazne HTTP konekcije i stavlja ih u red.
- **Radne niti (4)** — izvlače zahteve iz reda i obrađuju ih konkurentno.
- **RequestQueue** — ograničeni blokirajući red (maksimalno zahteva koliko se postavi u RequestQueue u Program.cs). Kada je pun, glavna nit blokira, primenjujući povratni pritisak.
- **Cache** — LRU keš u memoriji (maks. 100 unosa). Thread-safe; konkurentni zahtevi za isti ključ se dedupliraju — samo jedna nit pribaljva, ostale čekaju.
- **GitHubService** — prvo poziva `/stats/contributors` (brza putanja); fallback na `/commits` ako prvi endpoint vrati 202 ili prazne podatke.
- **Logger** — singleton, thread-safe, upisuje vremenske zapise u konzolu i `server.log`.

## Pokretanje

1. Kreirajte `.env` fajl u `GitHub_Commits/`:
   ```
   GH_TOKEN=vas_github_token
   ```
   Token nije obavezan za javne repozitorijume, ali sprečava ograničenje brzine zahteva.

2. Build i pokrenite:
   ```bash
   dotnet run --project GitHub_Commits 
   ```
  

3. Otvorite `http://localhost:1738` u browser-u.

## API

```
GET /?owner=<vlasnik>&repo=<repozitorijum>
```

Odgovor:
```json
{
  "owner": "tovalds",
  "repo": "linux",
  "contributors": [
    { "login": "torvalds", "commits": 42 }
  ],
  "totalCommits": 42,
  "fromCache": false,
  "totalTimeMs": 312.5
}
```

## Zahteva

- .NET 10
