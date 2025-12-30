<?php
declare(strict_types=1);

namespace App\Model\Table;

use Cake\ORM\Query\SelectQuery;
use Cake\ORM\RulesChecker;
use Cake\ORM\Table;
use Cake\Validation\Validator;

/**
 * Votesystem Model
 *
 * @method \App\Model\Entity\Votesystem newEmptyEntity()
 * @method \App\Model\Entity\Votesystem newEntity(array $data, array $options = [])
 * @method array<\App\Model\Entity\Votesystem> newEntities(array $data, array $options = [])
 * @method \App\Model\Entity\Votesystem get(mixed $primaryKey, array|string $finder = 'all', \Psr\SimpleCache\CacheInterface|string|null $cache = null, \Closure|string|null $cacheKey = null, mixed ...$args)
 * @method \App\Model\Entity\Votesystem findOrCreate($search, ?callable $callback = null, array $options = [])
 * @method \App\Model\Entity\Votesystem patchEntity(\Cake\Datasource\EntityInterface $entity, array $data, array $options = [])
 * @method array<\App\Model\Entity\Votesystem> patchEntities(iterable $entities, array $data, array $options = [])
 * @method \App\Model\Entity\Votesystem|false save(\Cake\Datasource\EntityInterface $entity, array $options = [])
 * @method \App\Model\Entity\Votesystem saveOrFail(\Cake\Datasource\EntityInterface $entity, array $options = [])
 * @method iterable<\App\Model\Entity\Votesystem>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Votesystem>|false saveMany(iterable $entities, array $options = [])
 * @method iterable<\App\Model\Entity\Votesystem>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Votesystem> saveManyOrFail(iterable $entities, array $options = [])
 * @method iterable<\App\Model\Entity\Votesystem>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Votesystem>|false deleteMany(iterable $entities, array $options = [])
 * @method iterable<\App\Model\Entity\Votesystem>|\Cake\Datasource\ResultSetInterface<\App\Model\Entity\Votesystem> deleteManyOrFail(iterable $entities, array $options = [])
 */
class VotesystemTable extends Table
{
    /**
     * Initialize method
     *
     * @param array<string, mixed> $config The configuration for the Table.
     * @return void
     */
    public function initialize(array $config): void
    {
        parent::initialize($config);

        $this->setTable('votesystem');
        $this->setDisplayField('Name');
        $this->setPrimaryKey('id');
    }

    /**
     * Default validation rules.
     *
     * @param \Cake\Validation\Validator $validator Validator instance.
     * @return \Cake\Validation\Validator
     */
    public function validationDefault(Validator $validator): Validator
    {
        $validator
            ->scalar('Name')
            ->maxLength('Name', 30)
            ->requirePresence('Name', 'create')
            ->notEmptyString('Name');

        $validator
            ->integer('Popularity')
            ->requirePresence('Popularity', 'create')
            ->notEmptyString('Popularity');

        return $validator;
    }
}
