-- Categoría pasa a ser opcional: las notas de Ámbito Nacional/Provincial no llevan categoría,
-- y una nota de Ámbito Partido también puede quedar sin categoría ("Ninguna").
ALTER TABLE notas_prensa_versiones MODIFY COLUMN CategoriaId INT NULL;
