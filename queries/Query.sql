SELECT DISTINCT json_each.value AS keyword
FROM Cards
JOIN json_each(Cards.keywords)
ORDER BY keyword;