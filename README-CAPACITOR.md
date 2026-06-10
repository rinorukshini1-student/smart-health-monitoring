# Smart Health — Aplikacion Tablet (Capacitor + Firebase)

Ky udhëzues shpjegon si ta instalosh aplikacionin Vue në tablet Android me **njoftime në kohë reale** përmes **SignalR** (app i hapur) dhe **Firebase Cloud Messaging** (app në background/mbyllur).

## Si funksionon

| Situata | Mekanizmi |
|---------|-----------|
| App i hapur | SignalR → njoftim lokal në tablet |
| App në background / mbyllur | Vue.Api → Firebase FCM → push notification |
| Regjistrimi i tabletit | FCM token → `POST /api/push/register` |

---

## 1. Parakushtet

- **Node.js 18+**
- **Android Studio** (për build APK)
- **Java JDK 17**
- **Firebase project** (console.firebase.google.com)
- Tablet dhe PC në **të njëjtin Wi‑Fi**

---

## 2. Firebase Console

1. Krijo projekt të ri Firebase (ose përdor ekzistues).
2. Shto app **Android** me package name: `com.smarthealth.monitoring`
3. Shkarko **`google-services.json`** → vendose te:
   ```
   vue-client/android/app/google-services.json
   ```
4. **Project Settings → Service accounts → Generate new private key**
5. Ruaj JSON si:
   ```
   Vue.Api/firebase-service-account.json
   ```
6. Në `Vue.Api/appsettings.json`:
   ```json
   "Firebase": {
     "Enabled": true,
     "ServiceAccountPath": "firebase-service-account.json",
     "ProjectId": "your-firebase-project-id"
   }
   ```

---

## 3. Konfiguro IP-në e serverit (tablet)

Tablet-i **nuk** mund të përdorë `localhost`. Gjej IP-në e PC-së:

```powershell
ipconfig
# p.sh. 192.168.1.50
```

Krijo `vue-client/.env.production`:

```env
VITE_API_BASE_URL=http://192.168.1.50:5099
```

Nis Vue.Api që dëgjon në të gjitha interface-et:

```powershell
dotnet run --project Vue.Api/Vue.Api.csproj --urls "http://0.0.0.0:5099"
```

Lejo portin **5099** në Windows Firewall.

---

## 4. Build & instalim në tablet

```powershell
cd vue-client
npm install
npm run cap:sync          # build + kopjon te android/
npx cap open android      # hap Android Studio
```

Në **Android Studio**:

1. Verifiko që `google-services.json` është në `android/app/`
2. Në `android/build.gradle` (project level), shto nëse mungon:
   ```gradle
   classpath 'com.google.gms:google-services:4.4.2'
   ```
3. Në `android/app/build.gradle`, në fund:
   ```gradle
   apply plugin: 'com.google.gms.google-services'
   ```
4. Për HTTP (jo HTTPS), në `AndroidManifest.xml` te `<application>`:
   ```xml
   android:usesCleartextTraffic="true"
   ```
5. **Run** në tablet ose **Build → Build APK**

---

## 5. Test njoftimesh

1. Nis Vue.Api me Firebase `Enabled: true`
2. Instalo APK në tablet
3. Hap app → prano lejet për njoftime
4. Kur vjen një alarm (SignalR / Kafka pipeline), duhet të shohësh:
   - Njoftim në app (foreground)
   - Push FCM (background)

Test i shpejtë API:

```powershell
curl http://192.168.1.50:5099/api/health
```

---

## Skriptet e dobishme

| Komanda | Përshkrimi |
|---------|------------|
| `npm run dev` | Dev web (port 5173, proxy te 5099) |
| `npm run build` | Build për Vue.Api wwwroot |
| `npm run build:mobile` | Build për Capacitor (`dist/`) |
| `npm run cap:sync` | Build + sync Android |
| `npm run cap:android` | Sync + hap Android Studio |

---

## Shënime

- **Pa Firebase**: njoftimet lokale nga SignalR funksionojnë kur app është i hapur.
- **Me Firebase**: push edhe kur app është mbyllur.
- Mos commit-o `google-services.json` ose `firebase-service-account.json` (janë në `.gitignore`).
