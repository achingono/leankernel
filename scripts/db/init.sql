SELECT 'CREATE DATABASE litellm'
WHERE NOT EXISTS (
	SELECT FROM pg_database WHERE datname = 'litellm'
)\gexec


SELECT 'CREATE DATABASE leankernel'
WHERE NOT EXISTS (
	SELECT FROM pg_database WHERE datname = 'leankernel'
)\gexec


SELECT 'CREATE DATABASE gbrain'
WHERE NOT EXISTS (
	SELECT FROM pg_database WHERE datname = 'gbrain'
)\gexec

