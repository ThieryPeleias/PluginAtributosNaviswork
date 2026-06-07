# Layout e Design System dos Painéis da Virtuart4D

Este documento detalha o funcionamento visual, a estrutura de layout e a estilização dos formulários no plugin **Virtuart4D Navisworks**. A interface foi projetada usando o paradigma **Windows Forms** tradicional em conjunto com um **Design System unificado** localizado na classe [UITheme.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/UITheme.cs).

---

## 1. O Design System (`UITheme.cs`)

Para evitar a dependência de estilos inline ou layouts inconsistentes, o visual de toda a aplicação é governado por tokens e componentes reutilizáveis definidos em `UITheme.cs`.

### Tokens de Cores (Color Palette)
*   **Background principal:** `F8F9FA` (Cinza claro neutro para o fundo da janela).
*   **Background secundário:** `F0F2F6` (Usado em campos desabilitados ou cabeçalhos de tabela).
*   **Superfície (Cards):** `FFFFFF` (Fundo de cartões/painéis de conteúdo).
*   **Primária (Epic Datasmith Cyan/Teal):** `#007586` (Cor de destaque para marcação, botões de ação e títulos).
*   **Sucesso (Verde):** `#1B8360` (Indicadores de sucesso e coordenadas capturadas).
*   **Erro/Perigo (Vermelho):** `#B71C1C` (Para exclusão de dados e botões de deletar).

### Tipografia
Utiliza a família de fontes **Segoe UI** dimensionada harmonicamente:
*   `H1` (18pt, Bold) — Títulos dos cabeçalhos.
*   `H2` (14pt, Bold) — Títulos de sub-painéis.
*   `H3` (12pt, Bold) — Títulos dentro de cartões de conteúdo.
*   `Body` (10pt, Regular) — Textos normais e inputs.
*   `BodySmall` (9pt, Regular) — Dicas e textos auxiliares de menor peso.
*   `Label` (9.5pt, Bold) — Textos de botão e etiquetas.

### Componentes Reutilizáveis do Tema
O tema expõe métodos estáticos que criam controles estilizados sob demanda:
*   `CreatePrimaryButton(text)`: Retorna um botão com fundo Teal primário e efeito hover.
*   `CreateSecondaryButton(text)`: Retorna um botão de borda com fundo branco e efeito hover sutil.
*   `CreateHeader(title, subtitle)`: Painel superior azul-petróleo de 60/90px de altura com título e subtítulo.
*   `CreateFooter()`: Painel inferior cinza de 52px de altura com borda superior fina para abrigar botões de confirmação.
*   `CreateCard()`: Painel branco com borda fina ideal para agrupar controles.
*   `CreateDivider()`: Linha de divisão horizontal fina.
*   `StyleTextBox(textBox)` & `StyleDataGridView(grid)`: Estilizam controles nativos do Windows Forms de acordo com o tema.

---

## 2. Painel 1: Configurações de Exportação (`ExportSettingsForm.cs`)

### Estrutura Visual
*   **Tamanho:** Fixo de `520x720` pixels.
*   **Disposição:** Empilhamento vertical simples.
*   **Estrutura de Controles:**
    1.  **Cabeçalho (`pnlHeader`):** Criado via `UITheme.CreateHeader`.
    2.  **Corpo da Janela (`pnlBody`):** Um painel com preenchimento (`Padding`) de 16px.
    3.  **Cartão de Conteúdo (`card`):** Um cartão branco `UITheme.CreateCard` que ocupa todo o corpo restante.
    4.  **Seções Internas do Cartão:**
        *   *Seção 1: Hierarchy Merging* (NumericUpDown para profundidade de merge).
        *   *Seção 2: Spatial Coordinate Origin* (3 Inputs X/Y/Z alinhados horizontalmente + 3 botões de captura e reset).
        *   *Seção 3: Smart Merging* (Botão para adicionar atributos e um `FlowLayoutPanel` para empilhar dinamicamente as linhas de Category/Property selecionadas).
    5.  **Rodapé (`pnlFooter`):** Contém os botões **Cancel** e **Export** alinhados à direita.

### Código de Inicialização do Layout (`ExportSettingsForm.cs`)

```csharp
private void InitializeUI()
{
    Text = "Virtuart4D - Export Settings";
    Size = new Size(520, 720);
    MinimumSize = new Size(520, 720);
    MaximumSize = new Size(520, 720);
    StartPosition = FormStartPosition.CenterScreen;
    FormBorderStyle = FormBorderStyle.FixedDialog;
    MaximizeBox = false;
    MinimizeBox = false;
    ShowInTaskbar = false;
    BackColor = UITheme.Color.Background;
    Font = UITheme.Typography.Body;

    // ── Header ─────────────────────────────────────────────────
    var pnlHeader = UITheme.CreateHeader("Datasmith Export Settings",
        "Configure hierarchy merging and spatial coordinate origin");
    Controls.Add(pnlHeader);

    // ── Footer ─────────────────────────────────────────────────
    var pnlFooter = UITheme.CreateFooter();
    
    btnCancel = UITheme.CreateSecondaryButton("Cancel");
    btnCancel.Width = 90;
    btnCancel.Location = new Point(310, 8);
    btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
    pnlFooter.Controls.Add(btnCancel);

    btnExport = UITheme.CreatePrimaryButton("Export");
    btnExport.Width = 90;
    btnExport.Location = new Point(410, 8);
    btnExport.Click += BtnExport_Click;
    pnlFooter.Controls.Add(btnExport);

    Controls.Add(pnlFooter);
    AcceptButton = btnExport;
    CancelButton = btnCancel;

    // ── Body / Content Card ────────────────────────────────────
    var pnlBody = new Panel
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(UITheme.Spacing.LG)
    };
    Controls.Add(pnlBody);
    pnlBody.BringToFront();

    var card = UITheme.CreateCard();
    card.Dock = DockStyle.Fill;
    card.Padding = new Padding(UITheme.Spacing.LG);
    pnlBody.Controls.Add(card);

    int y = UITheme.Spacing.MD;

    // ── Section 1: Hierarchy Merging ───────────────────────────
    var lblMergeTitle = new Label
    {
        Text = "HIERARCHY MERGING (MERGE DEPTH)",
        Font = UITheme.Typography.Label,
        ForeColor = UITheme.Color.Primary,
        Location = new Point(UITheme.Spacing.LG, y),
        AutoSize = true
    };
    card.Controls.Add(lblMergeTitle);
    y += 20;

    var lblMergeHint = new Label
    {
        Text = "0 = No Merging (preserves every component as separate mesh).\n3+ = High Merging (groups deep subtrees, fast and highly optimized).",
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.TextSecondary,
        Location = new Point(UITheme.Spacing.LG, y),
        Size = new Size(420, 32)
    };
    card.Controls.Add(lblMergeHint);
    y += 34;

    numMergeDepth = new NumericUpDown
    {
        Font = UITheme.Typography.Body,
        Location = new Point(UITheme.Spacing.LG, y),
        Width = 100,
        Minimum = 0,
        Maximum = 10,
        Value = _cachedMergeDepth
    };
    card.Controls.Add(numMergeDepth);
    y += 40;

    // ── Divider ────────────────────────────────────────────────
    var div = UITheme.CreateDivider();
    div.Location = new Point(UITheme.Spacing.LG, y);
    div.Width = 430;
    card.Controls.Add(div);
    y += 16;

    // ── Section 2: Spatial Coordinate Origin ───────────────────
    var lblOriginTitle = new Label
    {
        Text = "SPATIAL COORDINATE ORIGIN (X, Y, Z)",
        Font = UITheme.Typography.Label,
        ForeColor = UITheme.Color.Primary,
        Location = new Point(UITheme.Spacing.LG, y),
        AutoSize = true
    };
    card.Controls.Add(lblOriginTitle);
    y += 20;

    var lblOriginHint = new Label
    {
        Text = "Subtracts this reference point in Navisworks meters to ensure precision.\nBecomes (0, 0, 0) inside Unreal Engine.",
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.TextSecondary,
        Location = new Point(UITheme.Spacing.LG, y),
        Size = new Size(420, 32)
    };
    card.Controls.Add(lblOriginHint);
    y += 34;

    // Coord Inputs
    int lx = UITheme.Spacing.LG;
    
    var lblX = new Label { Text = "X (m):", Font = UITheme.Typography.BodySmall, ForeColor = UITheme.Color.TextPrimary, Location = new Point(lx, y), AutoSize = true };
    card.Controls.Add(lblX);
    txtOriginX = new TextBox { Location = new Point(lx, y + 16), Width = 100, Text = _cachedOriginX.ToString("F3") };
    UITheme.StyleTextBox(txtOriginX);
    card.Controls.Add(txtOriginX);
    lx += 115;

    var lblY = new Label { Text = "Y (m):", Font = UITheme.Typography.BodySmall, ForeColor = UITheme.Color.TextPrimary, Location = new Point(lx, y), AutoSize = true };
    card.Controls.Add(lblY);
    txtOriginY = new TextBox { Location = new Point(lx, y + 16), Width = 100, Text = _cachedOriginY.ToString("F3") };
    UITheme.StyleTextBox(txtOriginY);
    card.Controls.Add(txtOriginY);
    lx += 115;

    var lblZ = new Label { Text = "Z (m):", Font = UITheme.Typography.BodySmall, ForeColor = UITheme.Color.TextPrimary, Location = new Point(lx, y), AutoSize = true };
    card.Controls.Add(lblZ);
    txtOriginZ = new TextBox { Location = new Point(lx, y + 16), Width = 100, Text = _cachedOriginZ.ToString("F3") };
    UITheme.StyleTextBox(txtOriginZ);
    card.Controls.Add(txtOriginZ);
    
    y += 56;

    // Pick / Reset Buttons
    btnPickVertex = UITheme.CreatePrimaryButton("Pick Vertex with Snap");
    btnPickVertex.Width = 170;
    btnPickVertex.Location = new Point(UITheme.Spacing.LG, y);
    btnPickVertex.Click += BtnPickVertex_Click;
    card.Controls.Add(btnPickVertex);

    btnPickSelected = UITheme.CreatePrimaryButton("Pick Selection Center");
    btnPickSelected.Width = 170;
    btnPickSelected.Location = new Point(UITheme.Spacing.LG + 178, y);
    btnPickSelected.Click += BtnPickSelected_Click;
    card.Controls.Add(btnPickSelected);

    btnResetOrigin = UITheme.CreateSecondaryButton("Reset to Zero");
    btnResetOrigin.Width = 100;
    btnResetOrigin.Location = new Point(UITheme.Spacing.LG + 356, y);
    btnResetOrigin.Click += BtnResetOrigin_Click;
    card.Controls.Add(btnResetOrigin);

    y += 42;

    // Selected Item Info Label
    lblSelectedInfo = new Label
    {
        Text = "",
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.Success,
        Location = new Point(UITheme.Spacing.LG, y),
        Size = new Size(420, 20),
        AutoEllipsis = true
    };
    card.Controls.Add(lblSelectedInfo);
    
    UpdateSelectionInfo();
    y += 26;

    // ── Divider ──
    var div2 = UITheme.CreateDivider();
    div2.Location = new Point(UITheme.Spacing.LG, y);
    div2.Width = 430;
    card.Controls.Add(div2);
    y += 16;

    // ── Section 3: Group by Property ──
    var lblGroupTitle = new Label
    {
        Text = "SMART MERGING / GROUPING BY PROPERTIES",
        Font = UITheme.Typography.Label,
        ForeColor = UITheme.Color.Primary,
        Location = new Point(UITheme.Spacing.LG, y),
        Width = 280,
        AutoSize = true
    };
    card.Controls.Add(lblGroupTitle);

    btnAddProperty = UITheme.CreatePrimaryButton("+ Add Attribute");
    btnAddProperty.Width = 110;
    btnAddProperty.Height = 24;
    btnAddProperty.Font = UITheme.Typography.BodySmall;
    btnAddProperty.Location = new Point(UITheme.Spacing.LG + 300, y - 4);
    btnAddProperty.Click += (s, e) => AddPropertyRow();
    card.Controls.Add(btnAddProperty);

    y += 22;

    var lblGroupHint = new Label
    {
        Text = "Restructures udatasmith file: groups geometries under intermediate parent folders\nrepresenting unique combinations of attribute values (e.g. Set A | Phase 1).",
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.TextSecondary,
        Location = new Point(UITheme.Spacing.LG, y),
        Size = new Size(420, 32)
    };
    card.Controls.Add(lblGroupHint);
    y += 34;

    flpProperties = new FlowLayoutPanel
    {
        Location = new Point(UITheme.Spacing.LG, y),
        Width = 430,
        Height = 105,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true
    };
    card.Controls.Add(flpProperties);

    if (_cachedGroupByProperties != null && _cachedGroupByProperties.Count > 0)
    {
        foreach (var propPair in _cachedGroupByProperties)
        {
            AddPropertyRow(propPair[0], propPair[1]);
        }
    }
}
```

---

## 3. Painel 2: Edição de Atributos (`WriteAttributeForm.cs`)

### Estrutura Visual
*   **Tamanho:** Redimensionável. Padrão inicial de `980x640` com limite mínimo de `860x520` pixels.
*   **Disposição:** Painel Duplo Lado a Lado usando um controle `SplitContainer` para flexibilidade.
*   **Estrutura de Controles:**
    1.  **Cabeçalho (`pnlHeader`):** Criado via `UITheme.CreateHeader`. Inclui um rótulo dinâmico (`lblSubHeader`) com o resumo dos itens selecionados e sets ativos.
    2.  **Corpo (`pnlBody`):** Contém um `SplitContainer` preenchido a 60/40.
        *   **Painel Esquerdo (60% da Largura):** Grid de atributos customizados.
            *   *Storage Category TextBox (ReadOnly):* Mostra a categoria onde as propriedades serão gravadas.
            *   *Caixa de Ajuda Informacional:* Caixa em destaque com borda azul e fundo azul claro dando contexto ao usuário.
            *   *Barra de Ferramentas da Grid:* Botões rápidos (+ Add Row, - Remove, Clear Grid, Import, Export).
            *   *Grid Principal (DataGridView):* Colunas para Name, Value e Type (string, double, int, bool) customizadas através do tema.
        *   **Painel Direito (40% da Largura):** Lista de Seleção de Sets.
            *   *Sets selection count box:* Caixa informativa mostrando quantos sets estão selecionados do total de sets detectados.
            *   *Barra de Pesquisa (TextBox):* Barra dinâmica de filtragem em tempo real contendo um placeholder cinza que some ao receber foco.
            *   *Barra de Seleção Rápida:* Botões "Select All" e "Clear".
            *   *ListView com Checkboxes:* Lista de todos os Sets disponíveis no projeto com o respectivo número de itens de cada um e caixas de marcação (Checkboxes).
    3.  **Rodapé (`pnlFooter`):** Botões alinhados de forma responsiva nas extremidades:
        *   *Esquerda:* Botão de Perigo **Delete Attributes** (fundo vermelho com hover estilizado).
        *   *Direita:* Botões **Cancel** e **Save** (fundo primário) mantendo alinhamento à direita mesmo após redimensionamentos da janela.

### Código de Inicialização do Layout (`WriteAttributeForm.cs`)

```csharp
private void MontarLayout()
{
    Text = "Virtuart4D - Write Attribute";
    Size = new Size(980, 640);
    MinimumSize = new Size(860, 520);
    StartPosition = FormStartPosition.CenterScreen;
    BackColor = UITheme.Color.Background;
    Font = UITheme.Typography.Body;

    // ── Header ─────────────────────────────────────────────────
    pnlHeader = UITheme.CreateHeader("Write Attribute", "Save custom properties and selected set names to model elements");
    pnlHeader.Height = 100;

    foreach (Control ctrl in pnlHeader.Controls)
    {
        if (ctrl is Label lbl && lbl.Location.Y == 54)
        {
            lbl.Location = new Point(24, 46);
        }
    }
    
    lblSubHeader = new Label
    {
        Font = UITheme.Typography.BodySmall,
        ForeColor = Color.FromArgb(200, 240, 245),
        AutoSize = true,
        Location = new Point(24, 70),
        BackColor = Color.Transparent
    };
    pnlHeader.Controls.Add(lblSubHeader);
    Controls.Add(pnlHeader);

    // ── Footer ─────────────────────────────────────────────────
    var pnlFooter = UITheme.CreateFooter();

    var btnCancelar = UITheme.CreateSecondaryButton("Cancel");
    btnCancelar.DialogResult = DialogResult.Cancel;
    btnCancelar.Width = 100;
    btnCancelar.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
    btnCancelar.Location = new Point(pnlFooter.Width - 232, 8);
    pnlFooter.Controls.Add(btnCancelar);

    var btnGravar = UITheme.CreatePrimaryButton("Save");
    btnGravar.Font = UITheme.Typography.Label;
    btnGravar.Width = 100;
    btnGravar.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
    btnGravar.Location = new Point(pnlFooter.Width - 120, 8);
    btnGravar.Click += BtnGravar_Click;
    pnlFooter.Controls.Add(btnGravar);

    var btnExcluir = new Button
    {
        Text = "Delete Attributes",
        Font = UITheme.Typography.Label,
        ForeColor = Color.White,
        BackColor = UITheme.Color.Error,
        FlatStyle = FlatStyle.Flat,
        Height = 36,
        Width = 150,
        Cursor = Cursors.Hand,
        TextAlign = ContentAlignment.MiddleCenter,
        Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        Location = new Point(16, 8)
    };
    btnExcluir.FlatAppearance.BorderSize = 0;
    btnExcluir.MouseEnter += (s, e) => btnExcluir.BackColor = UITheme.Color.ErrorHover;
    btnExcluir.MouseLeave += (s, e) => btnExcluir.BackColor = UITheme.Color.Error;
    btnExcluir.Click += BtnExcluir_Click;
    pnlFooter.Controls.Add(btnExcluir);

    pnlFooter.Resize += (s, e) =>
    {
        btnGravar.Location = new Point(pnlFooter.Width - 120, 8);
        btnCancelar.Location = new Point(pnlFooter.Width - 232, 8);
    };

    Controls.Add(pnlFooter);

    // ── Main Content Container ─────────────────────────────────
    var pnlBody = new Panel
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(16, 12, 16, 12)
    };
    Controls.Add(pnlBody);
    pnlBody.BringToFront();

    // ── Splitter ───────────────────────────────────────────────
    var splitter = new SplitContainer
    {
        Dock = DockStyle.Fill,
        SplitterWidth = 12,
        BackColor = UITheme.Color.Background,
        BorderStyle = BorderStyle.None
    };
    splitter.Panel1.BackColor = UITheme.Color.Background;
    splitter.Panel2.BackColor = UITheme.Color.Background;
    pnlBody.Controls.Add(splitter);

    splitter.SplitterDistance = (int)(splitter.Width * 0.58);
    pnlBody.Resize += (s, e) => splitter.SplitterDistance = (int)(splitter.Width * 0.58);

    MontarPainelAtributos(splitter.Panel1);
    MontarPainelSets(splitter.Panel2);

    AtualizarResumoSetsUI();

    AcceptButton = btnGravar;
    CancelButton = btnCancelar;
}

private void MontarPainelAtributos(Control parent)
{
    pnlEsquerdo = UITheme.CreateCard();
    pnlEsquerdo.Dock = DockStyle.Fill;
    pnlEsquerdo.Padding = new Padding(16);
    parent.Controls.Add(pnlEsquerdo);

    var lblTitulo = new Label
    {
        Text = "Custom Attributes Grid",
        Font = UITheme.Typography.H3,
        ForeColor = UITheme.Color.Primary,
        Dock = DockStyle.Top,
        Height = 24
    };
    pnlEsquerdo.Controls.Add(lblTitulo);

    var pnlCat = new Panel
    {
        Dock = DockStyle.Top,
        Height = 36,
        Padding = new Padding(0, 4, 0, 4)
    };

    var lblCat = new Label
    {
        Text = "Storage Category:",
        Font = UITheme.Typography.Label,
        ForeColor = UITheme.Color.TextPrimary,
        AutoSize = true,
        Location = new Point(0, 8)
    };
    pnlCat.Controls.Add(lblCat);

    int catLabelW = TextRenderer.MeasureText("Storage Category:", UITheme.Typography.Label).Width + 8;

    txtCategoria = new TextBox
    {
        Text = CATEGORIA_FIXA,
        Font = UITheme.Typography.Body,
        BorderStyle = BorderStyle.FixedSingle,
        ReadOnly = true,
        BackColor = UITheme.Color.BackgroundSecondary,
        ForeColor = UITheme.Color.TextSecondary,
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        Location = new Point(catLabelW, 4),
        Size = new Size(pnlCat.Width - catLabelW - 80, 26)
    };
    pnlCat.Controls.Add(txtCategoria);

    var btnResetCat = UITheme.CreateSecondaryButton("Reset");
    btnResetCat.Font = UITheme.Typography.BodySmall;
    btnResetCat.Height = 26;
    btnResetCat.Width = 70;
    btnResetCat.Anchor = AnchorStyles.Right | AnchorStyles.Top;
    btnResetCat.Location = new Point(pnlCat.Width - 72, 4);
    pnlCat.Controls.Add(btnResetCat);

    pnlCat.Resize += (s, e) =>
    {
        txtCategoria.Width = pnlCat.Width - catLabelW - 80;
        btnResetCat.Location = new Point(pnlCat.Width - 72, 4);
    };

    pnlEsquerdo.Controls.Add(pnlCat);
    pnlCat.BringToFront();

    var pnlAjuda = new Panel
    {
        Dock = DockStyle.Top,
        Height = 46,
        BackColor = UITheme.Color.PrimaryVeryLight,
        Padding = new Padding(10, 6, 10, 6),
        Margin = new Padding(0, 4, 0, 8)
    };
    pnlAjuda.Paint += (s, e) =>
    {
        var rect = new Rectangle(0, 0, pnlAjuda.Width - 1, pnlAjuda.Height - 1);
        using (var pen = new Pen(UITheme.Color.PrimaryLight))
            e.Graphics.DrawRectangle(pen, rect);
    };

    var lblAjuda = new Label
    {
        Dock = DockStyle.Fill,
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.PrimaryHover,
        Text = $"Custom attributes entered below are written under '{CATEGORIA_FIXA}'. Names of selected sets are stored in the '{VirtuartSchema.PropriedadeSets}' property."
    };
    pnlAjuda.Controls.Add(lblAjuda);
    pnlEsquerdo.Controls.Add(pnlAjuda);
    pnlAjuda.BringToFront();

    var spacer = new Panel { Dock = DockStyle.Top, Height = 8 };
    pnlEsquerdo.Controls.Add(spacer);
    spacer.BringToFront();

    var pnlBtns = new Panel
    {
        Dock = DockStyle.Top,
        Height = 36,
        Padding = new Padding(0, 2, 0, 4)
    };

    var btnAdd = UITheme.CreatePrimaryButton("+ Add Row");
    btnAdd.Font = UITheme.Typography.BodySmall;
    btnAdd.Height = 28;
    btnAdd.Width = 95;
    btnAdd.Location = new Point(0, 2);
    btnAdd.Click += (s, e) => AdicionarLinhaGrid("", "", "string");
    pnlBtns.Controls.Add(btnAdd);

    var btnRemove = new Button
    {
        Text = "- Remove",
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.Error,
        BackColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Height = 28,
        Width = 85,
        Cursor = Cursors.Hand,
        Location = new Point(100, 2)
    };
    btnRemove.FlatAppearance.BorderColor = UITheme.Color.Error;
    btnRemove.MouseEnter += (s, e) => btnRemove.BackColor = UITheme.Color.ErrorLight;
    btnRemove.MouseLeave += (s, e) => btnRemove.BackColor = Color.White;
    btnRemove.Click += (s, e) =>
    {
        if (grid.SelectedRows.Count > 0 && grid.Rows.Count > 0)
            grid.Rows.RemoveAt(grid.SelectedRows[0].Index);
    };
    pnlBtns.Controls.Add(btnRemove);

    var btnLimpar = UITheme.CreateSecondaryButton("Clear Grid");
    btnLimpar.Font = UITheme.Typography.BodySmall;
    btnLimpar.Height = 28;
    btnLimpar.Width = 90;
    btnLimpar.Location = new Point(190, 2);
    btnLimpar.Click += (s, e) => grid.Rows.Clear();
    pnlBtns.Controls.Add(btnLimpar);

    var btnImportar = UITheme.CreateSecondaryButton("Import");
    btnImportar.Font = UITheme.Typography.BodySmall;
    btnImportar.Height = 28;
    btnImportar.Width = 75;
    btnImportar.Location = new Point(285, 2);
    btnImportar.Click += BtnImportar_Click;
    pnlBtns.Controls.Add(btnImportar);

    var btnExportar = UITheme.CreateSecondaryButton("Export");
    btnExportar.Font = UITheme.Typography.BodySmall;
    btnExportar.Height = 28;
    btnExportar.Width = 75;
    btnExportar.Location = new Point(365, 2);
    btnExportar.Click += BtnExportar_Click;
    pnlBtns.Controls.Add(btnExportar);

    pnlEsquerdo.Controls.Add(pnlBtns);
    pnlBtns.BringToFront();

    grid = new DataGridView
    {
        Dock = DockStyle.Fill,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowTemplate = { Height = 30 },
        ColumnHeadersHeight = 34,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
    };
    UITheme.StyleDataGridView(grid);

    grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nome", HeaderText = "Name", FillWeight = 40 });
    grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Valor", HeaderText = "Value", FillWeight = 40 });

    var colTipo = new DataGridViewComboBoxColumn
    {
        Name = "Tipo",
        HeaderText = "Type",
        FillWeight = 20,
        FlatStyle = FlatStyle.Flat,
        DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
    };
    colTipo.Items.AddRange("string", "double", "int", "bool");
    grid.Columns.Add(colTipo);

    pnlEsquerdo.Controls.Add(grid);
    grid.BringToFront();

    AdicionarLinhaGrid("", "", "string");
}

private void MontarPainelSets(Control parent)
{
    pnlDireito = UITheme.CreateCard();
    pnlDireito.Dock = DockStyle.Fill;
    pnlDireito.Padding = new Padding(16);
    parent.Controls.Add(pnlDireito);

    var lblTitulo = new Label
    {
        Text = "Detected Model Sets",
        Font = UITheme.Typography.H3,
        ForeColor = UITheme.Color.Primary,
        Dock = DockStyle.Top,
        Height = 24
    };
    pnlDireito.Controls.Add(lblTitulo);

    var lblDica = new Label
    {
        Text = "Mark sets that should be assigned to this item",
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.TextSecondary,
        Dock = DockStyle.Top,
        Height = 18
    };
    pnlDireito.Controls.Add(lblDica);
    lblDica.BringToFront();

    var pnlResumoSets = new Panel
    {
        Dock = DockStyle.Top,
        Height = 52,
        BackColor = UITheme.Color.BackgroundSecondary,
        Padding = new Padding(10, 6, 10, 6),
        Margin = new Padding(0, 4, 0, 6)
    };
    pnlResumoSets.Paint += (s, e) =>
    {
        var rect = new Rectangle(0, 0, pnlResumoSets.Width - 1, pnlResumoSets.Height - 1);
        using (var pen = new Pen(UITheme.Color.BorderLight))
            e.Graphics.DrawRectangle(pen, rect);
    };

    lblResumoSets = new Label
    {
        Dock = DockStyle.Fill,
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.TextPrimary,
        Text = ObterTextoResumoSets()
    };
    pnlResumoSets.Controls.Add(lblResumoSets);
    pnlDireito.Controls.Add(pnlResumoSets);
    pnlResumoSets.BringToFront();

    var pnlPesquisa = new Panel
    {
        Dock = DockStyle.Top,
        Height = 36,
        Padding = new Padding(0, 4, 0, 4)
    };

    txtPesquisa = new TextBox
    {
        Dock = DockStyle.Fill,
        Font = UITheme.Typography.Body,
        ForeColor = UITheme.Color.TextTertiary,
        Text = "Search sets...",
        BorderStyle = BorderStyle.FixedSingle
    };
    txtPesquisa.GotFocus += (s, e) =>
    {
        if (txtPesquisa.ForeColor == UITheme.Color.TextTertiary)
        {
            txtPesquisa.Text = "";
            txtPesquisa.ForeColor = UITheme.Color.TextPrimary;
        }
    };
    txtPesquisa.LostFocus += (s, e) =>
    {
        if (string.IsNullOrWhiteSpace(txtPesquisa.Text))
        {
            txtPesquisa.Text = "Search sets...";
            txtPesquisa.ForeColor = UITheme.Color.TextTertiary;
        }
    };
    txtPesquisa.TextChanged += (s, e) => PreencherSets();
    pnlPesquisa.Controls.Add(txtPesquisa);
    pnlDireito.Controls.Add(pnlPesquisa);
    pnlPesquisa.BringToFront();

    var pnlSelecaoSets = new Panel
    {
        Dock = DockStyle.Top,
        Height = 34,
        Padding = new Padding(0, 4, 0, 2)
    };

    var btnLimparSets = UITheme.CreateSecondaryButton("Clear");
    btnLimparSets.Font = UITheme.Typography.BodySmall;
    btnLimparSets.Height = 26;
    btnLimparSets.Width = 66;
    btnLimparSets.Dock = DockStyle.Right;
    btnLimparSets.Click += (s, e) => DefinirSelecaoSets(false);
    pnlSelecaoSets.Controls.Add(btnLimparSets);

    var btnSelecionarTodos = UITheme.CreateSecondaryButton("Select All");
    btnSelecionarTodos.Font = UITheme.Typography.BodySmall;
    btnSelecionarTodos.Height = 26;
    btnSelecionarTodos.Width = 84;
    btnSelecionarTodos.Dock = DockStyle.Right;
    btnSelecionarTodos.Margin = new Padding(0, 0, 8, 0);
    btnSelecionarTodos.Click += (s, e) => DefinirSelecaoSets(true);
    pnlSelecaoSets.Controls.Add(btnSelecionarTodos);

    lblSelecaoSets = new Label
    {
        Dock = DockStyle.Fill,
        Font = UITheme.Typography.BodySmall,
        ForeColor = UITheme.Color.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };
    pnlSelecaoSets.Controls.Add(lblSelecaoSets);

    pnlDireito.Controls.Add(pnlSelecaoSets);
    pnlSelecaoSets.BringToFront();

    lvCategorias = new ListView
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        CheckBoxes = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        BorderStyle = BorderStyle.FixedSingle,
        Font = UITheme.Typography.BodySmall,
        GridLines = false,
        MultiSelect = false,
        ShowItemToolTips = true
    };
    lvCategorias.Columns.Add("Set Name", 240);
    lvCategorias.Columns.Add("Elements", 70, HorizontalAlignment.Center);
    lvCategorias.ItemChecked += LvCategorias_ItemChecked;

    pnlDireito.Controls.Add(lvCategorias);
    lvCategorias.BringToFront();

    PreencherSets();
}
```
