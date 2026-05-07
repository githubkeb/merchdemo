select max("SortOrder") from "AggregateProducts" where "ProductCategoryId" = 2;
select * from "AggregateProducts" where "ProductCategoryId" = 1 fetch first 1 row only;
select max("Id") from "AggregateProducts";

select pg_is_in_recovery();

DO $$

DECLARE
start_id integer := (select max("Id") from "AggregateProducts") +1;
    v_merchant_id  integer := 1;
    v_category_id  integer := 2;
    v_start_sort   integer := (select max("SortOrder")
                               from "AggregateProducts"
                               where "ProductCategoryId" = v_category_id) + 1;
    v_count        integer := 10000;

    i integer;
    v_now timestamptz := now();
BEGIN
FOR i IN 0..(v_count - 1) LOOP
        INSERT INTO "AggregateProducts" (
            "Id",
            "MerchantId",
            "ProductCategoryId",
            "SortOrder",
            "Name",
            "Price",
            "LastAction",
            "LastOccurredAtUtc",
            "UpdatedAtUtc"
        )
        VALUES (
            start_id+i,
            v_merchant_id,
            v_category_id,
            v_start_sort + i,
            format('BulkProduct-%s', lpad((i + 1)::text, 5, '0')),
            99.99,
  'updated',
            v_now,
            v_now
        );
END LOOP;
    RAISE NOTICE 'done';
END
$$;
