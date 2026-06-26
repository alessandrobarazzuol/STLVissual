STL Visual Modern WPF - C# .NET 8

Versione moderna Windows del programma STL.

Tecnologie:
- C# .NET 8
- WPF
- Inno Setup

Funzioni:
- Interfaccia moderna stile Visual Studio.
- Bottoni colorati e differenziati.
- Visualizzazione grafica di list, vector, set, stack, queue, map.
- Guide modificabili e salvabili per ogni metodo.
- Se modifichi la guida di push_front e premi "Salva guida/metodo", il testo viene ricordato anche nei giorni successivi.
- Password iniziale: 20242lbg.
- OpenRouter API nascosta nelle impostazioni.
- Generazione codice C++ da consegna.
- Codice C++ colorato.
- Copia codice e salva .cpp.
- Scheda "Compila C++" con editor e compilazione tramite g++ se installato nel PATH.
- Setup Inno incluso.

Come compilare:
1. Installa .NET 8 SDK.
2. Apri la cartella STLVisualModernWPF.
3. Esegui build_publish.bat.
4. Trovi l'eseguibile in publish.

Come creare il setup:
1. Dopo build_publish.bat apri setup_inno.iss con Inno Setup.
2. Premi Compile.

Copyright (C) Alessandro Barazzuol


ATTENZIONE:
Prima di compilare il setup con Inno Setup devi eseguire 1_BUILD_PRIMA_DI_INNO.bat.
Altrimenti Inno non trova la cartella STLVisualModernWPF\publish.


VERSIONE v3 GUIDE COMPLETE:
- Guide già pronte per tutti i metodi principali.
- Aggiunto bottone Costruttori.
- Guide modificabili e salvabili metodo per metodo.


VERSIONE v4 OVERLOAD ESEGUIBILI:
- Aggiunta sezione Overload disponibili del metodo selezionato.
- Per insert/erase/resize/push/emplace ecc. puoi provare i singoli overload con bottoni dedicati.


VERSIONE v5:
- Overload con campi posizione/valore/quantità/range/chiave map.
- Layout overload sistemato.
- Aggiunta scheda Albero binario con visualizzazione grafica e visite.


VERSIONE v7:
- Corretto errore di compilazione della v6.
- Scroll STL/albero.
- Animazione visite albero funzionante.


VERSIONE v8:
- Corretto errore CS1513 parentesi graffa mancante.
- Albero binario ricostruito con codice C# pulito.


VERSIONE v9:
- Compilatore C++ usa file temporanei unici.
- STL ridimensionato fino a 20 nodi.
- Albero ridimensionato per stare meglio nell'area visibile.


VERSIONE v10:
- Corretto errore di compilazione CS0103 fontSize.


VERSIONE v11:
- Correzione definitiva errore CS0103 fontSize.


VERSIONE v12:
- Guide più complete.
- Editor C++ colorabile tipo VS Code.
- Albero con zoom e layout più compatto.
- Animazioni più veloci.


VERSIONE v13:
- Visite preorder/inorder/postorder più lente: 1200 ms per nodo.


VERSIONE v14:
- Aggiunti pulsanti Esporta guide e Importa guide vicino a Salva guida/metodo.
- Le guide personalizzate possono essere trasferite tra versioni diverse.


VERSIONE v16:
- Corretto errore compilazione visita manuale.
- Avanti visita e Reset visita funzionanti.


VERSIONE v17:
- Fix visite albero con valori duplicati: evidenzia il nodo specifico, non tutti i nodi con lo stesso valore.


VERSIONE v18:
- Corretto errore compilazione CS0019 List<TreeNodeDemo> / List<int>.


VERSIONE v19:
- Rimpiccioliti i rettangoli dei nodi nel visualizzatore STL.
- Migliore visualizzazione fino a circa 20 nodi.
