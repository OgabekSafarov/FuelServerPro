# FuelServerPro

TexnoUz BlueSky protokoli bo'yicha TRK yoqilg'i quyish kolonkalari bilan RS-485 orqali muloqot qiladigan .NET 10 API serveri. Server serial portni boshqaradi, 1..16 manzillardagi qurilmalarni qidiradi, holatini navbatma-navbat yangilaydi va HTTP orqali JSON javob qaytaradi.

> **Muhim xavfsizlik eslatmasi:** API’da autentifikatsiya yo'q. `/cmd/c3_start`, `/cmd/ca_stop`, doza o'rnatish va klapan boshqaruvi kabi yo'llar qurilmaga haqiqiy buyruq yuboradi. Serverni ochiq internetga yoki ishonchsiz tarmoqqa chiqarmang. `FUEL_SERVER_URL` ni `0.0.0.0` ga bog'lashdan oldin firewall, VPN yoki himoyalangan reverse proxy qo'llang.

## Mundarija

- [Imkoniyatlar](#imkoniyatlar)
- [Talablar](#talablar)
- [O'rnatish va ishga tushirish](#ornatish-va-ishga-tushirish)
- [Swagger va OpenAPI](#swagger-va-openapi)
- [Uskuna va serial port](#uskuna-va-serial-port)
- [Ishlash tartibi](#ishlash-tartibi)
- [HTTP API](#http-api)
- [Buyruqlar](#buyruqlar)
- [Holat bayti](#holat-bayti)
- [Protokol tafsilotlari](#protokol-tafsilotlari)
- [Javoblar va xatolar](#javoblar-va-xatolar)
- [Loglar](#loglar)
- [Loyiha tuzilishi](#loyiha-tuzilishi)
- [Cheklovlar va muammolarni aniqlash](#cheklovlar-va-muammolarni-aniqlash)

## Imkoniyatlar

- ASP.NET Core minimal API va Kestrel HTTP serveri.
- OpenAPI 3.1 hujjati va interaktiv Swagger UI.
- 9600 baud, 8 data bit, Even parity, 1 stop bit (9600 8E1) RS-485 serial aloqasi.
- `0xF5` kadr, 7-bit XOR CRC va big-endian BCD qiymatlarini tekshirish.
- 1..16 manzillarni qidirish va topilgan qurilmalarni fon rejimida navbatma-navbat so'roq qilish.
- Har bir qurilma uchun joriy holat, hajm, summa va narxni saqlash.
- Status, hajm/summa, narx, doza, nasos, hisoblagich, karta/ID, xato va solenoid buyruqlari.
- Kunlik log fayllari.

## Talablar

- Windows kompyuter va .NET 10 SDK.
- RS-485 adapter/konvertor hamda kolonkaning A/B liniyalariga to'g'ri ulangan kabel.
- Windows’da mavjud va boshqa dastur egallamagan COM port.
- Protokol bo'yicha sozlangan TRK qurilma manzili: `1..16`.
- Swagger UI’dagi CSS/JavaScript CDN’dan yuklanadi, shu sababli Swagger sahifasi to'liq ishlashi uchun brauzerda internet bo'lishi kerak. OpenAPI JSON endpointi lokal serverdan beriladi.

## O'rnatish va ishga tushirish

Repository papkasida PowerShell orqali:

```powershell
dotnet restore
dotnet build
dotnet run
```

Server odatda quyidagi manzilda tinglaydi:

```text
http://localhost:8088
```

Serverni to'xtatish uchun `Ctrl+C` bosing. Ilova yopilayotganda serial port va polling to'xtatiladi.

### Tinglash manzilini o'zgartirish

`FUEL_SERVER_URL` muhit o'zgaruvchisi server URL’ini o'zgartiradi:

```powershell
$env:FUEL_SERVER_URL = "http://localhost:8090"
dotnet run
```

Faqat ishonchli lokal tarmoqda boshqa qurilmalardan kirish kerak bo'lsa, masalan:

```powershell
$env:FUEL_SERVER_URL = "http://0.0.0.0:8088"
dotnet run
```

`0.0.0.0` serverni barcha tarmoq interfeyslarida tinglatadi. API’da login yoki token yo'qligi sababli buni internetga ochiq holda ishlatmang. Ish tugagach muhit o'zgaruvchisini tozalash:

```powershell
Remove-Item Env:FUEL_SERVER_URL
```

### COM portni tanlash

Ulanishda `port` berilmasa, `COM6` ishlatiladi. Qurilmangiz Device Manager’da, masalan, `COM4` bo'lib ko'rinsa, so'rovda `port=COM4` yuboring.

## Swagger va OpenAPI

- Swagger UI: [`http://localhost:8088/swagger`](http://localhost:8088/swagger)
- OpenAPI JSON: [`http://localhost:8088/openapi/v1.json`](http://localhost:8088/openapi/v1.json)
- `http://localhost:8088/` ildiz yo'li Swagger UI’ga yo'naltiradi.

Swagger’da endpointlarni ko'rish, query parametrlarini kiritish va **Try it out** orqali so'rov yuborish mumkin. Buyruq yo'llari haqiqiy kolonka bilan serial aloqa qiladi; ularni test qilish nasos yoki klapanni boshqarishi mumkin.

## Uskuna va serial port

Port sozlamalari kodda ataylab qat'iy belgilangan:

| Sozlama | Qiymat |
| --- | --- |
| Interfeys | RS-485, ikki simli yarim dupleks |
| Baud rate | 9600 |
| Data bits | 8 |
| Parity | Even |
| Stop bits | 1 |
| Format | 9600 8E1 |
| DTR / RTS | Yoqilgan |
| Javob kutish | Odatda 300 ms; scan so'rovi 250 ms |

Barcha serial o'qish/yozish amallari `MultiTrkManager` ichidagi bitta lock bilan ketma-ket bajariladi. Boshqa dastur COM portni band qilgan bo'lsa, ulanish muvaffaqiyatsiz bo'ladi.

## Ishlash tartibi

1. Server ishga tushganda Kestrel `8088` portni tinglaydi; serial port hali ochilmagan bo'ladi.
2. `/connect?port=COM6` chaqirilganda port ochiladi.
3. Ulanishdan so'ng fon vazifasi `1..16` manzillarni `0xD5` status buyrug'i bilan qidiradi.
4. Topilgan qurilmalar `Devices` kolleksiyasiga qo'shiladi. Polling vazifasi ularning holatini navbatma-navbat yangilaydi.
5. Qurilma javob bermasa `isConnected` false bo'ladi. Hajm/summa qiymatlari polling paytida holatga qarab yangilanadi.
6. `/disconnect` portni yopadi, fon pollingni bekor qiladi va qurilmalar holatini tozalaydi.

`/scan` qo'lda qayta qidiradi. Ulangan serial port bo'lmasa, server `503 Service Unavailable` qaytaradi.

## HTTP API

Barcha biznes endpointlari `GET` va `POST` metodlarini qabul qiladi; qiymatlar URL query parametrlarida beriladi. JSON request body talab qilinmaydi. API CORS uchun barcha originlarga ruxsat beradi, lekin bu autentifikatsiya o'rnini bosmaydi.

### Umumiy endpointlar

| Yo'l | Parametrlar | Vazifa |
| --- | --- | --- |
| `/` | — | Swagger UI’ga yo'naltiradi |
| `/connect` | `port` ixtiyoriy, standart `COM6` | Serial portni ochadi va avtomatik qidiruv/pollingni boshlaydi |
| `/disconnect` | — | Polling va serial portni to'xtatadi, device state’ni tozalaydi |
| `/scan` | — | `1..16` manzillarni qayta qidiradi |
| `/status` | `addr` | Bitta qurilma statusini yangilab qaytaradi |
| `/status_all` | — | Topilgan barcha qurilmalar uchun keshlangan statusni qaytaradi |

`addr` qurilma manzili bo'lib, `1` dan `16` gacha bo'lishi kerak. `/cmd/a1_select_nozzle` dagi `nozzle` ham shu manzil oralig'ida bo'lishi kerak.

### Qurilmaga buyruq endpointlari

Quyidagi buyruq endpointlarining barchasi `addr` parametrini talab qiladi.

| Yo'l | Qo'shimcha parametr | Protokol | Vazifa |
| --- | --- | --- | --- |
| `/cmd/d5_status` | — | `0xD5` | Holat baytini o'qiydi |
| `/cmd/d9_read_volume` | — | `0xD9` | Joriy hajm va summani o'qiydi |
| `/cmd/b6_read_price` | — | `0xB6` | Birlik narxini o'qiydi |
| `/cmd/b2_set_price` | `price` | `0xB2` | Narxni o'rnatadi; `0..99999` butun son |
| `/cmd/b5_set_preset_amount` | `amount` | `0xB5` | Summa bo'yicha doza; `0..999999` butun son |
| `/cmd/b9_set_preset_volume` | `liters` | `0xB9` | Hajm bo'yicha doza; manfiy bo'lmasin, ko'pi bilan 2 kasr xona |
| `/cmd/c3_start` | — | `0xC3` | Quyishni boshlaydi, status ACK talab qiladi |
| `/cmd/ca_stop` | — | `0xCA` | Quyishni to'xtatadi |
| `/cmd/ba_pause` | — | `0xBA` | Quyishni pauza qiladi |
| `/cmd/b3_resume` | — | `0xB3` | Pauzadagi quyishni davom ettiradi |
| `/cmd/c5_read_total_counters` | — | `0xC5` | Umumiy hisoblagich: 6 bayt hajm + 6 bayt summa |
| `/cmd/c7_read_shift_counters` | — | `0xC7` | Smena hisoblagichi: 6 bayt hajm + 6 bayt summa |
| `/cmd/ea_clear_shift_counters` | — | `0xEA` | Smena hisoblagichini tozalaydi |
| `/cmd/a1_select_nozzle` | `nozzle` | `0xA1` | Rukav manzilini tanlaydi; status ACK talab qiladi |
| `/cmd/a7_read_preset` | — | `0xA7` | Prednabor turi va qiymatini o'qiydi |
| `/cmd/a8_read_card_id` | — | `0xA8` | 4 baytli karta ID’sini o'qiydi |
| `/cmd/a9_read_error` | — | `0xA9` | Xato kodini o'qiydi; `0` xato yo'qligini bildiradi |
| `/cmd/ab_clear_error` | — | `0xAB` | Xato bayrog'ini tozalaydi; status ACK talab qiladi |
| `/cmd/d7_read_device_id` | — | `0xD7` | 4 baytli qurilma ID’sini o'qiydi |
| `/cmd/aa_clear_preset_flag` | — | `0xAA` | Klaviaturadagi prednabor bayrog'ini tozalaydi |
| `/cmd/d6_read_solenoid` | — | `0xD6` | Solenoid/klapan javobining 2 baytini o'qiydi |
| `/cmd/d2_set_solenoid` | `state` | `0xD2` | Klapan holatini bitta bayt bilan yozadi; `0..255` |

### Misollar

COM portni ochish:

```http
GET http://localhost:8088/connect?port=COM6
```

Yoki PowerShell’dan:

```powershell
Invoke-RestMethod "http://localhost:8088/connect?port=COM6"
```

Qurilmalarni qidirish va statusni ko'rish:

```http
GET http://localhost:8088/scan
GET http://localhost:8088/status_all
GET http://localhost:8088/status?addr=1
```

Hajm o'qish, narx yozish va 50.00 litr prednabor berish:

```http
GET http://localhost:8088/cmd/d9_read_volume?addr=1
GET http://localhost:8088/cmd/b2_set_price?addr=1&price=5450
GET http://localhost:8088/cmd/b9_set_preset_volume?addr=1&liters=50.00
```

Quyishni boshqarish:

```http
GET http://localhost:8088/cmd/c3_start?addr=1
GET http://localhost:8088/cmd/ba_pause?addr=1
GET http://localhost:8088/cmd/b3_resume?addr=1
GET http://localhost:8088/cmd/ca_stop?addr=1
```

Ish tugagach:

```http
GET http://localhost:8088/disconnect
```

> `start`, `stop`, doza, narx, hisoblagichni tozalash va solenoid buyruqlarini haqiqiy uskuna ulanmagan paytda sinamang. Swagger’dagi **Try it out** ham real HTTP so'rov yuboradi.

## Holat bayti

`0xD5` javobidagi status byte bitmask hisoblanadi. Status JSON’da ham xom `stateByte`, ham quyidagi boolean qiymatlar qaytadi:

| Bit | Niqob | JSON maydoni | Ma'nosi |
| --- | --- | --- | --- |
| 7 | `0x80` | `isNozzleHanged`, `isNozzleOff` | Rukav ko'tarilgan holati. `isNozzleHanged` bit 1 bo'lganda true; `isNozzleOff` uning teskarisi |
| 6 | `0x40` | `isPaused` | Quyish pauzada |
| 5 | `0x20` | `isFilling` | Quyish davom etyapti |
| 3 | `0x08` | `isRemoteControl` | Kompyuter/remote boshqaruv rejimi |
| 1 | `0x02` | `isPresetReady` | Klaviaturadan prednabor tayyor |
| 0 | `0x01` | `hasError` | Qurilmada xato bayrog'i bor |

Bit 4 va bit 2 ushbu serverda talqin qilinmaydi; ularning qiymatini `stateByte` orqali ko'rish mumkin.

Status javobi namunasi:

```json
{
  "address": 1,
  "isConnected": true,
  "stateByte": 8,
  "isNozzleOff": true,
  "isNozzleHanged": false,
  "isFilling": false,
  "isPaused": false,
  "isRemoteControl": true,
  "isPresetReady": false,
  "hasError": false,
  "currentVolume": 50.0,
  "currentAmount": 272500.0,
  "price": 5450
}
```

## Protokol tafsilotlari

### Kadr formati

```text
F5 | ADDR | LEN | DATA... | CMD | CRC
```

- `F5` — kadr boshlanish bayti.
- `ADDR` — qurilma manzili (`1..16`).
- `LEN` — yuqori nibble `0xA`; pastki nibble `DATA + CMD + CRC` baytlar soni.
- `DATA` — buyruqqa tegishli ma'lumot; so'rovda bo'sh bo'lishi mumkin.
- `CMD` — buyruq kodi.
- `CRC` — undan oldingi baytlarning XOR natijasi, `& 0x7F` bilan 7 bitga cheklanadi.

Masalan, 50.00 litr prednabor `0xB9` buyrug'i bilan 4 bayt BCD shaklida uzatiladi. Har bir javob ishlatilishidan oldin start byte, length, CRC, yuborilgan address va CMD bilan tekshiriladi.

### BCD va birliklar

BCD’da har bir bayt ikkita o'nlik raqamni saqlaydi va katta xona birinchi keladi. Server noto'g'ri BCD nibble’larni va maydonga sig'maydigan qiymatlarni qabul qilmaydi.

| Qiymat | Hajm | API’dagi izoh |
| --- | --- | --- |
| Narx (`0xB6`, `0xB2`) | 3 bayt | JSON’da butun son sifatida qaytadi/yuboriladi |
| Hajm va doza (`0xD9`, `0xB9`) | 4 bayt | Hajm 0.01 litr aniqligida, ichki qiymat `100` ga ko'paytiriladi |
| Summa va doza (`0xD9`, `0xB5`) | 4 bayt | Doza qiymati BCD’dan oldin `100` ga ko'paytiriladi |
| Umumiy/smena hisoblagichlari | Har biri 6 bayt | Hajm va summa alohida 6 bayt BCD; JSON’da hajm/summa `/100` qilinadi |

### Javob turi

- Umumiy tasdiq: ma'lumotlarsiz `F5 ADDR A2 CMD CRC`.
- Status tasdiqi: bir bayt natija bilan `F5 ADDR A3 STATE CMD CRC`; muvaffaqiyat kodi `0x59`.
- Read buyruqlar tegishli uzunlikdagi `DATA` qaytaradi. Server javob CMD va manzilini so'rov bilan solishtiradi.
- `0xD5` holat bayti `DATA` qismida; CMD bayti emas.

Write endpoint faqat o'z buyruq turi kutadigan umumiy yoki status javobni olganda `success: true` qaytaradi.

## Javoblar va xatolar

Muvaffaqiyatli javoblar `application/json` formatida va odatda HTTP `200` bilan qaytadi. Misollar:

```json
{"success":true,"port":"COM6"}
```

```json
{"success":false,"error":"addr must be a device address from 1 to 16."}
```

| HTTP kodi | Qachon qaytadi |
| --- | --- |
| `200` | So'rov bajarildi; `success` odatda `true` |
| `400` | Parametr yo'q, noto'g'ri formatda yoki ruxsat etilgan oraliqdan tashqarida |
| `404` | Qurilma topilmadi yoki `/status` qurilmadan javob olmadi; noto'g'ri yo'l |
| `409` | Start/select/solenoid kabi status ACK talab qiladigan buyruq rad etildi yoki vaqt tugadi |
| `503` | COM port ochilmagan yoki ulanish amalga oshmadi |
| `504` | Qurilmadan kutilgan javob kelmadi yoki javob uzunligi/BCD tekshiruvdan o'tmadi |

JSON xatolarida `success: false` va `error` matni bo'ladi. HTTP status kodi muvaffaqiyatni tekshirish uchun asosiy belgi hisoblanadi.

## Loglar

Logger har kuni alohida faylga yozadi:

```text
C:\FuelServerLogs\log_YYYY-MM-DD.txt
```

Har bir yozuvda sana, vaqt va hodisa/xato matni bo'ladi. Faylga yozish muvaffaqiyatsiz bo'lsa, logger ilovani to'xtatmaydi. Log papkasiga yozish uchun Windows’da ruxsat bo'lishi kerak.

## Loyiha tuzilishi

| Fayl | Vazifasi |
| --- | --- |
| `Program.cs` | ASP.NET Core/Kestrel host, port, CORS, OpenAPI, manager lifetime |
| `ApiEndpoints.cs` | HTTP route’lari, Swagger metadata, parametr tekshiruvi va JSON javoblar |
| `MultiTrkManager.cs` | Serial port, qidiruv, polling, buyruqlar va javob tekshiruvi |
| `BlueSkyProtocol.cs` | Kadr yaratish, CRC, BCD encode/decode va frame validation |
| `TrkDevice.cs` | Qurilma statusi, bitlar va oxirgi o'lchovlar modeli |
| `Logger.cs` | Kunlik xavfsiz fayl logi |
| `FuelServerPro.csproj` | .NET 10 va package reference’lar |

## Cheklovlar va muammolarni aniqlash

### `/connect` `success: false` qaytaradi

- Windows Device Manager’dan COM port nomini tekshiring.
- Adapter portini boshqa terminal yoki dastur ishlatmayotganini tekshiring.
- `port` query parametrini aniq kiriting, masalan `COM4`.
- Windows log papkasiga yozishga ruxsat borligini tekshiring: `C:\FuelServerLogs`.

### `/scan` bo'sh `found` qaytaradi

- `/connect` muvaffaqiyatli bo'lganini va adapter RX/TX liniyalari to'g'ri ulanganini tekshiring.
- 9600 8E1, umumiy signal reference va RS-485 A/B qutblanishini tekshiring.
- TRK qurilmasi manzili `1..16` oralig'ida va aynan bir marta sozlangan bo'lishi kerak.
- RS-485 magistralda bir nechta qurilma javoblari bir-biriga to'qnashmayotganini tekshiring.

### Buyruq timeout yoki noto'g'ri response beradi

- Uskuna ulangan va buyruq uchun kerakli holatda ekanini tekshiring. Masalan, `0xC3` start uchun oldindan doza berilgan bo'lishi kerak.
- Noto'g'ri CRC, manzil, CMD yoki payload uzunlikdagi javoblar ataylab qabul qilinmaydi.
- Status, umumiy ACK va xato javobi formatlarini qurilma protokoli bilan solishtiring.
- Serial javob vaqti qurilma/adapterga bog'liq; kodda odatiy kutish 300 ms, scan’da 250 ms.

### Muhim ishlatish cheklovlari

- Bitta serial portga faqat shu server egalik qilishi kerak.
- Kadr tuzilishi va CRC’ni qurilma protokolisiz o'zgartirmang.
- `GET` so'rovlari ham mutatsiya qiluvchi komandalarni chaqira oladi; ularni brauzer preview, prefetch yoki tashqi monitoring tizimlaridan himoya qiling.
- Server va GitHub kodi ochiq bo'lishi mumkin, ammo real portga kirish huquqini tarmoq/firewall bilan cheklang.
- Qurilma bilan fizik test qilinmaguncha protokol buyruqlarining real kolonkadagi ACK xatti-harakatini tasdiqlangan deb hisoblamang.
