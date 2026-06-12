# Relatório de Tentativas: Agrupamento Inteligente (Smart Merging) e Mesclagem de Geometria

Este documento registra todas as tentativas de implementação, análises técnicas de arquivos exportados, erros identificados no XML e as limitações físicas impostas pelo formato de exportação da Epic Games Datasmith.

---

## 1. O Objetivo do Usuário
* Ao exportar um modelo no Navisworks com **Merge Depth = 5**, o exportador deve agrupar e mesclar fisicamente os elementos filhos com base em uma propriedade customizada (ex: `Custom.Virtuart-COD`).
* **Exemplo:** Se o nó pai `Viga_Pre-Moldada_0` possui vigas filhas com a propriedade `ValueY` e outras com `ValueZ`, o resultado final no Unreal deve conter dois elementos mesclados: `Viga_Pre-Moldada_ValueY` e `Viga_Pre-Moldada_ValueZ`, mantendo os seus respectivos atributos e unindo a geometria física (malhas).

---

## 2. Histórico de Tentativas e Resultados de Exportação

### Tentativa 1: Exportação Padrão (Sem Smart Merge)
* **Arquivo:** `merge normal.udatasmith` (e logs correspondentes)
* **Como funciona:** O plugin C# delega a exportação diretamente para a DLL C++ da Epic Games (`DatasmithNavisworksExporter.EpicGames`) passando o parâmetro `Merge=5`.
* **Resultado:**
  * O plugin C++ da Epic realiza a mesclagem física das geometrias. Ele junta todas as vigas sob a pasta de profundidade 5 em um único arquivo binário `.udsmesh` (`747d64ec3194becd93246dfd0c15ac55.udsmesh`).
  * Apenas o ator `Viga_Pre-Moldada_0` sobrevive no XML. As propriedades individuais das vigas filhas (como `ValueY` e `ValueZ`) são **completamente perdidas**, pois a malha foi fundida antes que pudéssemos ler os atributos no pós-processamento.
  * O arquivo final fica pequeno (225 KB) porque as malhas foram fundidas em poucos assets.

### Tentativa 2: Smart Merging com Restruturação XML (Merge=0 no Exporter)
* **Arquivo:** `Smart merginrg 2.udatasmith`
* **Como funciona:** Para evitar que o plugin C++ da Epic funda as vigas antes que possamos ler seus atributos, o C# delega a exportação com `Merge=0` (mantendo todas as 19.776 instâncias de geometria separadas). O pós-processador C# lê os metadados das vigas, agrupa as vigas com mesmos valores de `Virtuart-COD` sob novas pastas (ex: `Viga_Pre-Moldada_ValueY`) e anexa os meshes a elas.
* **Resultado / Falhas Identificadas:**
  * **Bug de Casing no XML:** O plugin da Epic gera contêineres `<children>` (em minúsculas). Nosso C# buscava por `"Children"` (com C maiúsculo), resultando na criação de tags `<Children>` duplicadas e na falha da limpeza recursiva de pastas vazias (`CleanAllEmptyActors`).
  * **Clutter do Outliner:** O Unreal importou uma estrutura poluída com contêineres duplicados e pastas vazias antigas.
  * **Problema de Geometria:** Embora organizadas sob as pastas corretas, as vigas **não foram mescladas fisicamente** (continuaram sendo milhares de objetos separados), pois estavam apontando para arquivos `.udsmesh` separados gerados pela Epic.

### Tentativa 3: Correção de Casing, Pruning e Estrutura XML Limpa
* **Arquivo:** `Com smarth merge 3.udatasmith`
* **Como funciona:** Corrigimos no C# a busca do contêiner de filhos para tratar o caso de forma insensível a maiúsculas/minúsculas (`children`) e consertamos a recursão de remoção de pastas vazias.
* **Resultado:**
  * O arquivo XML foi pós-processado com **sucesso estrutural total**: não há nenhuma tag `<Children>` duplicada, e todas as pastas vazias foram removidas.
  * Os elementos foram agrupados sob os atores de grupo de forma limpa (ex: `Group_..._door` com label `OCA_POR_ABRIR_2_FOLHAS_door`).
  * **Entretanto, a limitação de geometria permanece:** As vigas filhas continuam importando como objetos separados no Unreal (dentro da pasta de agrupamento), sem fusão física das malhas.

---

## 3. A Limitação Técnica Incontornável

A fusão (mesclagem) física de geometrias em arquivos `.udsmesh` é executada de forma **proprietária e compilada em C++** pelo plugin oficial da Epic Games. Nós não possuímos acesso ou biblioteca em C# capaz de abrir, ler, mesclar e reescrever os arquivos binários de malha `.udsmesh`.

Portanto, no fluxo híbrido atual, existem apenas dois caminhos:
1. **Mesclar fisicamente sem critério (Via Epic C++):** Passando `Merge=5`, a Epic mescla todas as vigas em um único asset, perdendo as propriedades individuais de agrupamento.
2. **Agrupar por propriedade no XML (Via C#):** Passando `Merge=0`, nós identificamos as propriedades, criamos as pastas de agrupamento corretas no Unreal e removemos o lixo de pastas vazias. Contudo, as vigas continuam sendo objetos individuais na cena do Unreal, o que pode impactar a performance.

---

## 4. Alternativas de Solução

Para obter os objetos **mesclados fisicamente** no Unreal respeitando a propriedade customizada:

1. **Mesclagem Manual ou Automática no Unreal Engine:**
   * O arquivo `Com smarth merge 3.udatasmith` agrupa perfeitamente as vigas nas pastas corretas do Outliner (`Viga_Pre-Moldada_ValueY`).
   * No Unreal, você pode selecionar o ator da pasta (ex: `Viga_Pre-Moldada_ValueY`), clicar com o botão direito e usar a ferramenta nativa do Unreal **Merge Actors** (ou criar um script de Editor Utility em Blueprints/Python no Unreal) para fundi-las em uma única malha de forma automatizada após o import.
2. **Reorganização Prévia no Navisworks (Inviável via código):**
   * O exportador C++ da Epic lê a árvore de seleção do modelo. Se pudéssemos mover os elementos e criar as pastas `Viga_Pre-Moldada_ValueY` na árvore do Navisworks antes de exportar, a Epic mesclaria perfeitamente. No entanto, a API do Navisworks não permite modificar ou reordenar a árvore de itens de modelos abertos (ela é estritamente somente-leitura).
