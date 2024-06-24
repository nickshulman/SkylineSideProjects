SELECT i.Name, i.File, i.Comment, i.Value as English, ja.Value as Japanese 
FROM InvariantResource i 
LEFT JOIN LocalizedResource ja ON i.Id = ja.InvariantResourceId AND ja.Language = 'ja' 
LEFT JOIN LocalizedResource zh ON i.Id = zh.InvariantResourceId AND zh.Language = 'zh-CHS'
WHERE i.Name NOT LIKE '>>%' AND i.Name NOT LIKE '$%' AND i.Type is NULL AND i.MimeType is NULL;
