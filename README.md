## Kako pokrenuti
Treba imati instaliran [.NET 10 SDK](https://dotnet.microsoft.com/download) ili noviji. U mapi
projekta (gdje je ova datoteka) pokrenuti:

```bash
dotnet run --project src/SmartLogistics.Web
```

i otvoriti http://localhost:5006 u pregledniku.

Testovi se pokreću s:

```bash
dotnet test
```


## Opis optimizacijske logike
Zamislio sam da kamion vozi i da u svakom trenutku zapisujem dvije stvari:

1. što je s kojim strojem: još čeka, u kamionu je ili je već dostavljen
2. gdje se kamion trenutno nalazi

To zovem situacija. Na primjer: "N01 je dostavljen, N02 je u kamionu, ostali čekaju,
kamion je na L3".

Iz svake situacije kamion može napraviti jedan korak: pokupiti stroj koji čeka (ako
stane) ili dostaviti stroj koji vozi. Svaki korak košta onoliko kilometara koliko ima do
te lokacije.

Program kreće od početka (sve čeka, kamion je u bazi) i redom prolazi kroz sve situacije
do kojih se može doći. Za svaku pamti najkraći put kojim je do nje stigao. Ako do iste
situacije dođe drugim, dužim putem, taj put odbacuje. Zato je brz: ne mora pamtiti sve
moguće redoslijede, nego samo najbolji do svake situacije.

Kad je kamion prazan, to može biti kraj rute. Program tada doda povratak u bazu, provjeri
je li sve stalo u 9 sati i zapamti: za ovu grupu naloga najkraća ruta ima toliko km.

Tako za svaki kamion znam najkraću rutu za svaku moguću grupu naloga. Na kraju program
isproba sve načine na koje se nalozi mogu podijeliti između kamiona A i B i uzme podjelu
s najmanje kilometara. Ako se neki nalog nikako ne može izvršiti, prednost ima raspored
koji izvrši što više naloga, a za ostale se prikaže upozorenje s razlogom.

Budući da program ništa ne preskače nasumce, dobiveni raspored je stvarno najbolji
mogući, a ne samo dovoljno dobar.

Google OR-TOOLS
OR-Tools je besplatna Googleova biblioteka za planiranje ruta. Ona ne isprobava sve
mogućnosti, nego brzo složi neko rješenje i onda ga popravlja dok ne istekne zadano
vrijeme (kod mene 5 sekundi). Zato je dobra za velike probleme, ali ne garantira
najbolje rješenje.


## Testovi
Svi testovi su u `tests/SmartLogistics.Tests` i pokreću se s `dotnet test`.

Testovi poslovnih pravila (validator mora uhvatiti svako kršenje), u `ValidatorTests.cs`:

- `Detects_weight_overload` – kamion A s 3.700 kg tereta prelazi nosivost od 3.500 kg
- `Detects_too_many_machines` – kamion B s 4 stroja, a smije 3
- `Detects_delivery_before_pickup` – stroj dostavljen prije nego je preuzet
- `Detects_machine_left_on_truck` – kamion se vratio u bazu sa strojem
- `Detects_shift_overrun` – ruta traje dulje od radnog vremena

Testovi rješenja, u `SolverTests.cs`:

- za podatke iz zadatka izvršeno je svih 10 naloga s 200,1 km
- teret i broj strojeva nikad ne prelaze ograničenja ni u jednom koraku
- svaki stroj preuzima i dostavlja isti kamion, i to tim redom
- svaki kamion kreće iz baze i vraća se u nju unutar 9 sati
- pretežak stroj i nalog koji ne stane u radno vrijeme prijavljuju se kao upozorenje
- na 25 nasumičnih malih primjera rezultat je isti kao kad se isprobaju sve kombinacije


## Korištenje AI alata
Koristio sam Claude (Claude Code), alat koji radi direktno u projektu: čita i piše
datoteke i pokreće naredbe. Pomogao mi je da razložim zadatak, predložio pristup i
napisao veći dio koda i testova. Ja sam određivao što se radi, donosio
odluke o pretpostavkama i provjeravao rezultate.

Rezultatima nisam vjerovao na riječ. Provjeravao sam ih ovako:

- ručno sam izračunao nekoliko udaljenosti (npr. L0 do L1 je √73 ≈ 8,5 km) i usporedio
  s programom
- validator je namjerno odvojen od solvera, pa ruta koju solver složi mora
  proći neovisnu provjeru svih pravila
- testovi uspoređuju solver s pretragom koja isproba baš sve kombinacije, a ona za
  provjeru koristi samo validator, ne kod solvera
- isti problem riješio sam i Google OR-Toolsom i dobio isti rezultat
- aplikaciju sam isprobao ručno: smanjio radno vrijeme na 5 h, stavio stroj teži od
  10.000 kg i provjerio da se pojave točna upozorenja

Tijekom provjere našao sam nekoliko stvari koje je trebalo popraviti. Rezultat u kojem kamion A vozi samo jedan nalog isprva mi je izgledao sumnjivo, pa sam zbog toga dodao i mogućnost provjere rješenja OR-Toolsima. Kod radnog vremena od 5 h OR-Tools je zapeo na 212 km umjesto 203,8 km, pa sam uključio Large Neighborhood Search i sada nađe dobru vrijednost.


## Utrošeno vrijeme
Ukupno oko 4 sata:

- čitanje zadatka i odabir pristupa: oko 1h
- pisanje koda (Claude) - oko 30 min
- testovi i provjera rezultata: oko 2h
- dokumentacija: oko 30 min
